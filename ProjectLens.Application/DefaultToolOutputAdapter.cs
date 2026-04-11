using System.Text;
using System.Text.Json;
using ProjectLens.Application.Abstractions;
using ProjectLens.Domain;

namespace ProjectLens.Application;

public sealed class DefaultToolOutputAdapter : IToolOutputAdapter
{
    private const string ReadFileToolName = "read_file";
    private const string SearchFilesToolName = "search_files";

    private readonly IEvidenceQualityEvaluator? _evidenceQualityEvaluator;
    private readonly IFileCompressor? _fileCompressor;
    private readonly IRecoveryPolicy _recoveryPolicy;

    public DefaultToolOutputAdapter(
        IFileCompressor? fileCompressor = null,
        IEvidenceQualityEvaluator? evidenceQualityEvaluator = null,
        IRecoveryPolicy? recoveryPolicy = null)
    {
        _fileCompressor = fileCompressor;
        _evidenceQualityEvaluator = evidenceQualityEvaluator;
        _recoveryPolicy = recoveryPolicy ?? new DefaultRecoveryPolicy(evidenceQualityEvaluator);
    }

    public ToolOutputAdapterResult BuildToolConversationOutput(
        ModelToolCall toolCall,
        ToolExecutionResult executionResult,
        string userPrompt,
        AggregatedEvidenceContext? aggregatedEvidenceContext)
    {
        if (!executionResult.Success)
        {
            return new ToolOutputAdapterResult(
                new ToolAdaptationResult(
                    executionResult.ErrorMessage ?? "Tool execution failed.",
                    Array.Empty<EvidenceItem>()),
                aggregatedEvidenceContext);
        }

        var adaptation = CreateToolContextOutput(
            toolCall.ToolName,
            executionResult.Output,
            userPrompt,
            aggregatedEvidenceContext,
            out var updatedAggregatedEvidenceContext);

        return new ToolOutputAdapterResult(adaptation, updatedAggregatedEvidenceContext);
    }

    private ToolAdaptationResult CreateToolContextOutput(
        string toolName,
        string? rawToolOutput,
        string userPrompt,
        AggregatedEvidenceContext? aggregatedEvidenceContext,
        out AggregatedEvidenceContext? updatedAggregatedEvidenceContext)
    {
        updatedAggregatedEvidenceContext = aggregatedEvidenceContext;
        if (string.IsNullOrWhiteSpace(rawToolOutput))
        {
            return new ToolAdaptationResult(string.Empty, Array.Empty<EvidenceItem>());
        }

        if (string.Equals(toolName, ReadFileToolName, StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseReadFilePayload(rawToolOutput, out var path, out var content, out var isTruncated, out var characterCount))
            {
                return new ToolAdaptationResult(
                    rawToolOutput,
                    [CreateToolObservation(toolName, toolName, rawToolOutput, isPartial: false)]);
            }

            var compressedOutput = _fileCompressor?.Compress(path, content, userPrompt) ?? rawToolOutput;
            var evidenceBasis = isTruncated
                ? $"Evidence basis: read_file returned truncated content at {characterCount} characters; recommendations should stay grounded to that partial excerpt."
                : $"Evidence basis: read_file returned a bounded excerpt of {characterCount} characters; use observed facts from this excerpt and label broader refactor ideas as inferred.";
            var baseOutput = $"{compressedOutput}{Environment.NewLine}{evidenceBasis}";
            updatedAggregatedEvidenceContext = UpdateAggregatedEvidenceFromRead(
                aggregatedEvidenceContext,
                path,
                baseOutput);
            var promptText = AppendAggregatedEvidenceContext(baseOutput, updatedAggregatedEvidenceContext);
            var evidenceKind = _fileCompressor is null
                ? EvidenceKind.DirectSnippet
                : EvidenceKind.FileSummary;
            var evidence = new EvidenceItem(
                toolName,
                path,
                CreateSnippet(_fileCompressor is null ? content : compressedOutput),
                evidenceKind,
                isTruncated || _fileCompressor is not null,
                isTruncated ? 0.8 : 1.0);

            return new ToolAdaptationResult(promptText, [evidence]);
        }

        if (string.Equals(toolName, SearchFilesToolName, StringComparison.OrdinalIgnoreCase))
        {
            updatedAggregatedEvidenceContext = BuildAggregatedEvidenceContext(
                rawToolOutput,
                userPrompt,
                aggregatedEvidenceContext);
            var searchAdaptation = SummarizeSearchResults(rawToolOutput, userPrompt, _evidenceQualityEvaluator);
            var promptText = AppendAggregatedEvidenceContext(searchAdaptation.PromptText, updatedAggregatedEvidenceContext);
            return searchAdaptation with { PromptText = promptText };
        }

        return new ToolAdaptationResult(
            rawToolOutput,
            [CreateToolObservation(toolName, toolName, rawToolOutput, isPartial: false)]);
    }

    private static ToolAdaptationResult SummarizeSearchResults(
        string rawToolOutput,
        string userPrompt,
        IEvidenceQualityEvaluator? evidenceQualityEvaluator)
    {
        try
        {
            using var document = JsonDocument.Parse(rawToolOutput);
            var root = document.RootElement;
            var query = root.TryGetProperty("Query", out var queryElement) ? queryElement.GetString() : null;
            var totalMatches = root.TryGetProperty("TotalMatches", out var totalMatchesElement)
                ? totalMatchesElement.GetInt32()
                : 0;
            var retrievalMode = root.TryGetProperty("RetrievalMode", out var retrievalModeElement)
                ? retrievalModeElement.GetString() ?? "keyword"
                : "keyword";
            var requestedStrategy = root.TryGetProperty("RequestedStrategy", out var requestedStrategyElement)
                ? requestedStrategyElement.GetString() ?? "keyword"
                : "keyword";
            var effectiveStrategy = root.TryGetProperty("EffectiveStrategy", out var effectiveStrategyElement)
                ? effectiveStrategyElement.GetString() ?? retrievalMode
                : retrievalMode;
            var keywordMatchCount = root.TryGetProperty("KeywordMatchCount", out var keywordCountElement)
                ? keywordCountElement.GetInt32()
                : totalMatches;
            var semanticMatchCount = root.TryGetProperty("SemanticMatchCount", out var semanticCountElement)
                ? semanticCountElement.GetInt32()
                : 0;
            var matches = root.TryGetProperty("Matches", out var matchesElement)
                ? matchesElement.EnumerateArray()
                    .Select(match => new EvidenceMatch(
                        match.TryGetProperty("Path", out var pathElement) ? pathElement.GetString() ?? string.Empty : string.Empty,
                        match.TryGetProperty("Snippet", out var snippetElement) ? snippetElement.GetString() ?? string.Empty : string.Empty,
                        match.TryGetProperty("LineNumber", out var lineNumberElement) ? lineNumberElement.GetInt32() : 0,
                        match.TryGetProperty("MatchKind", out var matchKindElement) ? matchKindElement.GetString() ?? "keyword" : "keyword",
                        match.TryGetProperty("SimilarityScore", out var similarityElement) ? similarityElement.GetDouble() : 0,
                        match.TryGetProperty("EndLineNumber", out var endLineNumberElement) ? endLineNumberElement.GetInt32() : 0))
                    .Where(match => !string.IsNullOrWhiteSpace(match.Path))
                    .ToArray()
                : Array.Empty<EvidenceMatch>();
            var searchEvidence = evidenceQualityEvaluator is null
                ? new SearchEvidenceAssessment(matches.Take(5).ToArray(), false, matches.Any(), string.Empty)
                : evidenceQualityEvaluator.AssessSearchEvidence(matches, query ?? userPrompt, 5);
            var evidenceItems = searchEvidence.RankedMatches
                .Select(match => new EvidenceItem(
                    SearchFilesToolName,
                    match.Path,
                    match.Snippet,
                    EvidenceKind.SearchHit,
                    true,
                    match.MatchKind.Equals("semantic", StringComparison.OrdinalIgnoreCase)
                        ? Math.Clamp(match.SimilarityScore, 0, 1)
                        : 1.0))
                .ToArray();

            var builder = new StringBuilder();
            builder.AppendLine($"search_files query: {query}");
            builder.AppendLine($"Total matches: {totalMatches}");
            builder.AppendLine($"Requested strategy: {requestedStrategy}");
            builder.AppendLine($"Effective strategy: {effectiveStrategy} (keyword candidates: {keywordMatchCount}, semantic candidates: {semanticMatchCount})");
            builder.AppendLine("Evidence basis: search_files returns bounded filename/snippet candidates only; semantic matches come from chunk-level similarity and still require read_file before making file-level claims.");

            if (searchEvidence.IsWeakEvidence)
            {
                builder.AppendLine(searchEvidence.RecoveryGuidance);
            }

            foreach (var match in searchEvidence.RankedMatches)
            {
                var semanticSuffix = match.MatchKind.Equals("semantic", StringComparison.OrdinalIgnoreCase)
                    ? $" [semantic {match.SimilarityScore:F2}]"
                    : string.Empty;
                builder.AppendLine($"- {match.Path}{semanticSuffix}: {match.Snippet}");
            }

            return new ToolAdaptationResult(builder.ToString().TrimEnd(), evidenceItems);
        }
        catch
        {
            return new ToolAdaptationResult(
                rawToolOutput,
                [CreateToolObservation(SearchFilesToolName, SearchFilesToolName, rawToolOutput, isPartial: false)]);
        }
    }

    private static bool TryParseReadFilePayload(
        string rawToolOutput,
        out string path,
        out string content,
        out bool isTruncated,
        out int characterCount)
    {
        path = string.Empty;
        content = string.Empty;
        isTruncated = false;
        characterCount = 0;

        try
        {
            using var document = JsonDocument.Parse(rawToolOutput);
            var root = document.RootElement;
            path = root.GetProperty("Path").GetString() ?? string.Empty;
            content = root.GetProperty("Content").GetString() ?? string.Empty;
            isTruncated = root.TryGetProperty("IsTruncated", out var isTruncatedElement) && isTruncatedElement.GetBoolean();
            characterCount = root.TryGetProperty("CharacterCount", out var characterCountElement)
                ? characterCountElement.GetInt32()
                : content.Length;
            return !string.IsNullOrWhiteSpace(path);
        }
        catch
        {
            return false;
        }
    }

    private AggregatedEvidenceContext? BuildAggregatedEvidenceContext(
        string rawToolOutput,
        string userPrompt,
        AggregatedEvidenceContext? existingContext)
    {
        if (_evidenceQualityEvaluator is null || !_recoveryPolicy.RequiresMultiFileSynthesis(userPrompt))
        {
            return existingContext;
        }

        try
        {
            using var document = JsonDocument.Parse(rawToolOutput);
            var root = document.RootElement;
            if (!root.TryGetProperty("Matches", out var matchesElement))
            {
                return existingContext;
            }

            var query = root.TryGetProperty("Query", out var queryElement) ? queryElement.GetString() : null;
            var allMatches = matchesElement.EnumerateArray()
                .Select(match => new EvidenceMatch(
                    match.TryGetProperty("Path", out var pathElement) ? pathElement.GetString() ?? string.Empty : string.Empty,
                    match.TryGetProperty("Snippet", out var snippetElement) ? snippetElement.GetString() ?? string.Empty : string.Empty,
                    match.TryGetProperty("LineNumber", out var lineNumberElement) ? lineNumberElement.GetInt32() : 0))
                .Where(match => !string.IsNullOrWhiteSpace(match.Path))
                .ToArray();
            var rankedMatches = _evidenceQualityEvaluator
                .RankMatches(allMatches, query ?? userPrompt, Math.Min(20, Math.Max(allMatches.Length, 5)));
            var isFeatureTracingPrompt = _evidenceQualityEvaluator.IsFeatureTracingPrompt(userPrompt);

            var selectedPaths = rankedMatches
                .Where(match => _evidenceQualityEvaluator.IsMeaningfulSourcePath(match.Path))
                .GroupBy(match => match.Path, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderByDescending(match => GetAggregationPriority(match.Path, match.Snippet, userPrompt, _evidenceQualityEvaluator))
                .ThenByDescending(match => _evidenceQualityEvaluator.ScoreFile(match.Path, match.Snippet, query ?? userPrompt))
                .ThenBy(match => match.Path, StringComparer.OrdinalIgnoreCase)
                .Select(match => match.Path)
                .Take(3)
                .ToArray();

            if (selectedPaths.Length < 2)
            {
                return existingContext;
            }

            var selectedFiles = selectedPaths
                .Select((path, index) => new AggregatedEvidenceFile(
                    path,
                    index == 0
                        ? "Likely main flow file based on the strongest meaningful source match for the current request."
                        : "Supporting source file selected as additional relevant evidence for the current request.",
                    existingContext?.Files.FirstOrDefault(file =>
                        string.Equals(file.Path, path, StringComparison.OrdinalIgnoreCase))?.ObservationSummary ?? string.Empty))
                .ToArray();

            var evidenceLimitations = new List<string>
            {
                $"Selection is based on ranked search matches and snippets; {CountObservedFiles(selectedFiles)} of {selectedFiles.Length} selected file(s) have been read so far."
            };

            if (isFeatureTracingPrompt && CountObservedFiles(selectedFiles) < Math.Min(2, selectedFiles.Length))
            {
                evidenceLimitations.Add("Feature flow is still being traced; the current main-flow file is provisional until more supporting files are read.");
            }

            return new AggregatedEvidenceContext(
                isFeatureTracingPrompt,
                isFeatureTracingPrompt,
                selectedFiles[0].Path,
                selectedFiles,
                evidenceLimitations);
        }
        catch
        {
            return existingContext;
        }
    }

    private static AggregatedEvidenceContext? UpdateAggregatedEvidenceFromRead(
        AggregatedEvidenceContext? aggregatedEvidenceContext,
        string path,
        string baseOutput)
    {
        if (aggregatedEvidenceContext is null)
        {
            return null;
        }

        var updatedFiles = aggregatedEvidenceContext.Files
            .Select(file => string.Equals(file.Path, path, StringComparison.OrdinalIgnoreCase)
                ? file with { ObservationSummary = CreateSnippet(baseOutput) }
                : file)
            .ToArray();

        if (updatedFiles.All(file => string.IsNullOrWhiteSpace(file.ObservationSummary)))
        {
            return aggregatedEvidenceContext;
        }

        var evidenceLimitations = new[]
        {
            $"Multi-file aggregation currently covers {CountObservedFiles(updatedFiles)} of {updatedFiles.Length} selected file(s)."
        }.Concat(aggregatedEvidenceContext.EvidenceLimitations
            .Where(limitation => limitation.Contains("provisional", StringComparison.OrdinalIgnoreCase)))
        .Distinct(StringComparer.Ordinal)
        .ToArray();

        var isProvisional = aggregatedEvidenceContext.IsFeatureTrace && CountObservedFiles(updatedFiles) < 2;
        if (!isProvisional)
        {
            evidenceLimitations = evidenceLimitations
                .Where(limitation => !limitation.Contains("provisional", StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        return aggregatedEvidenceContext with
        {
            IsProvisional = isProvisional,
            Files = updatedFiles,
            EvidenceLimitations = evidenceLimitations
        };
    }

    private static string AppendAggregatedEvidenceContext(
        string output,
        AggregatedEvidenceContext? aggregatedEvidenceContext)
    {
        if (aggregatedEvidenceContext is null)
        {
            return output;
        }

        var builder = new StringBuilder(output.TrimEnd());
        builder.AppendLine();
        builder.AppendLine("Aggregation context:");

        if (aggregatedEvidenceContext.IsFeatureTrace)
        {
            builder.AppendLine($"Feature flow confidence: {(aggregatedEvidenceContext.IsProvisional ? "provisional" : "strong")}");
        }

        if (!string.IsNullOrWhiteSpace(aggregatedEvidenceContext.LikelyMainFlowFile))
        {
            builder.AppendLine($"Likely main flow file: {aggregatedEvidenceContext.LikelyMainFlowFile}");
        }

        foreach (var file in aggregatedEvidenceContext.Files)
        {
            if (string.Equals(file.Path, aggregatedEvidenceContext.LikelyMainFlowFile, StringComparison.OrdinalIgnoreCase))
            {
                builder.AppendLine($"Selected file: {file.Path} | reason: {file.SelectionReason}");
            }
            else
            {
                builder.AppendLine($"Supporting file: {file.Path} | reason: {file.SelectionReason}");
            }

            if (!string.IsNullOrWhiteSpace(file.ObservationSummary))
            {
                builder.AppendLine($"Observed file summary: {file.Path} => {file.ObservationSummary}");
            }
        }

        foreach (var limitation in aggregatedEvidenceContext.EvidenceLimitations)
        {
            builder.AppendLine($"Aggregation limitation: {limitation}");
        }

        return builder.ToString().TrimEnd();
    }

    private static int CountObservedFiles(IEnumerable<AggregatedEvidenceFile> files)
    {
        return files.Count(file => !string.IsNullOrWhiteSpace(file.ObservationSummary));
    }

    private static int GetAggregationPriority(
        string path,
        string snippet,
        string userPrompt,
        IEvidenceQualityEvaluator evidenceQualityEvaluator)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(userPrompt))
        {
            return 0;
        }

        var lowerPath = path.ToLowerInvariant();
        var lowerPrompt = userPrompt.ToLowerInvariant();
        var score = 0;
        var isFeatureTracingPrompt = evidenceQualityEvaluator.IsFeatureTracingPrompt(userPrompt);
        var intentTerms = evidenceQualityEvaluator.ExpandIntentTerms(userPrompt);
        var matchedIntentTerms = intentTerms.Count(term =>
            lowerPath.Contains(term, StringComparison.Ordinal) ||
            snippet.Contains(term, StringComparison.OrdinalIgnoreCase));

        if (lowerPrompt.Contains("flow", StringComparison.Ordinal) ||
            lowerPrompt.Contains("architecture", StringComparison.Ordinal) ||
            lowerPrompt.Contains("explain", StringComparison.Ordinal) ||
            lowerPrompt.Contains("how", StringComparison.Ordinal))
        {
            if (lowerPath.Contains("controller", StringComparison.Ordinal) ||
                lowerPath.Contains("program", StringComparison.Ordinal) ||
                lowerPath.Contains("startup", StringComparison.Ordinal) ||
                lowerPath.Contains("handler", StringComparison.Ordinal) ||
                lowerPath.Contains("service", StringComparison.Ordinal) ||
                lowerPath.Contains("manager", StringComparison.Ordinal) ||
                lowerPath.Contains("orchestrator", StringComparison.Ordinal))
            {
                score += 40;
            }

            if (lowerPath.Contains("repository", StringComparison.Ordinal))
            {
                score += 5;
            }

            if (lowerPath.Contains("helper", StringComparison.Ordinal) ||
                lowerPath.Contains("util", StringComparison.Ordinal) ||
                lowerPath.Contains("utility", StringComparison.Ordinal))
            {
                score -= 25;
            }
        }

        if (isFeatureTracingPrompt)
        {
            score += matchedIntentTerms * 10;

            if (lowerPath.Contains("controller", StringComparison.Ordinal))
            {
                score += 30;
            }

            if (lowerPath.Contains("service", StringComparison.Ordinal))
            {
                score += 26;
            }

            if (lowerPath.Contains("entity", StringComparison.Ordinal) ||
                lowerPath.Contains("model", StringComparison.Ordinal))
            {
                score += 18;
            }

            if (lowerPath.EndsWith("app.jsx", StringComparison.Ordinal) ||
                lowerPath.EndsWith("app.tsx", StringComparison.Ordinal) ||
                lowerPath.EndsWith("app.js", StringComparison.Ordinal) ||
                lowerPath.EndsWith("app.ts", StringComparison.Ordinal) ||
                lowerPath.Contains("/src/", StringComparison.Ordinal))
            {
                score += 16;
            }

            if (lowerPath.EndsWith("program.cs", StringComparison.Ordinal) ||
                lowerPath.EndsWith("startup.cs", StringComparison.Ordinal) ||
                lowerPath.Contains("launchsettings", StringComparison.Ordinal) ||
                lowerPath.Contains("/config/", StringComparison.Ordinal) ||
                lowerPath.Contains("/configuration/", StringComparison.Ordinal))
            {
                score -= 45;
            }
        }

        return score;
    }

    private static EvidenceItem CreateToolObservation(
        string toolName,
        string sourceId,
        string content,
        bool isPartial)
    {
        return new EvidenceItem(
            toolName,
            sourceId,
            CreateSnippet(content),
            EvidenceKind.ToolObservation,
            isPartial,
            0.7);
    }

    private static string CreateSnippet(string content)
    {
        var normalized = string.Join(
            ' ',
            content.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        const int maxLength = 220;
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return normalized[..maxLength].TrimEnd() + "...";
    }
}
