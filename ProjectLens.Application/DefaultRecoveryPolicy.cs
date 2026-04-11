using System.Text;
using System.Text.Json;
using ProjectLens.Application.Abstractions;
using ProjectLens.Domain;

namespace ProjectLens.Application;

public sealed class DefaultRecoveryPolicy : IRecoveryPolicy
{
    private const string ReadFileToolName = "read_file";
    private const string SearchFilesToolName = "search_files";

    private readonly IEvidenceQualityEvaluator? _evidenceQualityEvaluator;

    public DefaultRecoveryPolicy(IEvidenceQualityEvaluator? evidenceQualityEvaluator = null)
    {
        _evidenceQualityEvaluator = evidenceQualityEvaluator;
    }

    public FinalAnswerDecision EvaluateFinalAnswer(
        string? finalAnswer,
        IReadOnlyCollection<ModelToolCall> toolCalls,
        RecoveryState recoveryState,
        AggregatedEvidenceContext? aggregatedEvidenceContext,
        string userPrompt)
    {
        if (string.IsNullOrWhiteSpace(finalAnswer) || toolCalls.Count > 0)
        {
            return new FinalAnswerDecision(false, false, null, null);
        }

        if (recoveryState.HasPendingWeakSearchEvidence)
        {
            return new FinalAnswerDecision(
                true,
                false,
                null,
                BuildWeakSearchRecoveryPrompt(recoveryState.WeakSearchRecoveryGuidance));
        }

        if (RequiresMoreMultiFileEvidence(aggregatedEvidenceContext, userPrompt))
        {
            return new FinalAnswerDecision(
                true,
                false,
                null,
                BuildMultiFileAggregationPrompt(aggregatedEvidenceContext!));
        }

        return new FinalAnswerDecision(true, true, finalAnswer, null);
    }

    public string BuildDuplicateToolCallMessage(ModelToolCall toolCall, AgentSessionState? sessionState)
    {
        var builder = new StringBuilder();
        builder.Append("This exact tool call was already executed earlier, so rerunning it is unlikely to add new evidence. ");
        builder.Append("Reuse the prior tool output already in the conversation. ");
        builder.Append("Choose a different action if you still need more evidence. ");
        builder.Append("If you answer now from partial evidence, separate observed facts from inferred recommendations. ");

        if (string.Equals(toolCall.ToolName, SearchFilesToolName, StringComparison.OrdinalIgnoreCase))
        {
            builder.Append("Prefer read_file on the most relevant matched or recently visited file, or provide the best grounded answer now if the current evidence is sufficient. ");
        }
        else if (string.Equals(toolCall.ToolName, ReadFileToolName, StringComparison.OrdinalIgnoreCase))
        {
            builder.Append("Use the prior compressed file summary for reasoning, inspect a different relevant file, or provide the best grounded answer with any uncertainty called out. ");
        }
        else
        {
            builder.Append("Choose a different action or provide the best grounded answer possible. ");
        }

        if (sessionState is not null)
        {
            if (sessionState.VisitedFiles.Count > 0)
            {
                builder.Append($"Recently visited files: {string.Join(", ", sessionState.VisitedFiles.TakeLast(5))}. ");
            }

            if (!string.IsNullOrWhiteSpace(sessionState.WorkingSummary))
            {
                builder.Append($"Working summary: {CreateSnippet(sessionState.WorkingSummary)}");
            }
        }

        return builder.ToString().Trim();
    }

    public RecoveryState UpdateWeakSearchState(
        RecoveryState currentState,
        string toolName,
        string? rawToolOutput,
        string userPrompt)
    {
        if (string.Equals(toolName, SearchFilesToolName, StringComparison.OrdinalIgnoreCase))
        {
            var searchEvidence = AnalyzeSearchEvidence(rawToolOutput, userPrompt);
            return new RecoveryState(
                searchEvidence?.IsWeakEvidence == true,
                searchEvidence?.RecoveryGuidance);
        }

        if (string.Equals(toolName, ReadFileToolName, StringComparison.OrdinalIgnoreCase) &&
            _evidenceQualityEvaluator is not null &&
            TryParseReadFilePayload(rawToolOutput ?? string.Empty, out var path, out _, out _, out _) &&
            _evidenceQualityEvaluator.IsMeaningfulSourcePath(path))
        {
            return RecoveryState.Empty;
        }

        return currentState;
    }

    public bool RequiresMultiFileSynthesis(string userPrompt)
    {
        if (string.IsNullOrWhiteSpace(userPrompt))
        {
            return false;
        }

        var normalizedPrompt = userPrompt.ToLowerInvariant();
        var synthesisSignals = new[]
        {
            "flow", "logic", "architecture", "explain", "how", "refactor", "structure", "interaction", "pipeline"
        };

        return synthesisSignals.Any(signal => normalizedPrompt.Contains(signal, StringComparison.Ordinal));
    }

    private SearchEvidenceAssessment? AnalyzeSearchEvidence(
        string? rawToolOutput,
        string userPrompt)
    {
        if (string.IsNullOrWhiteSpace(rawToolOutput))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(rawToolOutput);
            var root = document.RootElement;
            var matches = root.TryGetProperty("Matches", out var matchesElement)
                ? matchesElement.EnumerateArray()
                    .Select(match => new EvidenceMatch(
                        match.TryGetProperty("Path", out var pathElement) ? pathElement.GetString() ?? string.Empty : string.Empty,
                        match.TryGetProperty("Snippet", out var snippetElement) ? snippetElement.GetString() ?? string.Empty : string.Empty,
                        match.TryGetProperty("LineNumber", out var lineNumberElement) ? lineNumberElement.GetInt32() : 0))
                    .Where(match => !string.IsNullOrWhiteSpace(match.Path))
                    .ToArray()
                : Array.Empty<EvidenceMatch>();
            var query = root.TryGetProperty("Query", out var queryElement) ? queryElement.GetString() : null;

            return _evidenceQualityEvaluator is null
                ? new SearchEvidenceAssessment(matches.Take(5).ToArray(), false, matches.Any(), string.Empty)
                : _evidenceQualityEvaluator.AssessSearchEvidence(matches, query ?? userPrompt, 5);
        }
        catch
        {
            return null;
        }
    }

    private string BuildWeakSearchRecoveryPrompt(string? recoveryGuidance = null)
    {
        var guidance = string.IsNullOrWhiteSpace(recoveryGuidance)
            ? "Prefer one bounded recovery step: broaden the search with related implementation terms or inspect a likely main source file before answering."
            : recoveryGuidance.Trim();

        return $"The previous exact keyword search was weak evidence for a logic explanation. Do not finalize yet. {guidance}";
    }

    private bool RequiresMoreMultiFileEvidence(
        AggregatedEvidenceContext? aggregatedEvidenceContext,
        string userPrompt)
    {
        if (aggregatedEvidenceContext is null || !RequiresMultiFileSynthesis(userPrompt))
        {
            return false;
        }

        var selectedFileCount = aggregatedEvidenceContext.Files.Count;
        if (selectedFileCount < 2)
        {
            return false;
        }

        var observedFileCount = aggregatedEvidenceContext.Files.Count(file =>
            !string.IsNullOrWhiteSpace(file.ObservationSummary));

        var requiredObservedFiles = Math.Min(2, selectedFileCount);

        return observedFileCount < requiredObservedFiles;
    }

    private static string BuildMultiFileAggregationPrompt(AggregatedEvidenceContext aggregatedEvidenceContext)
    {
        var unreadSupportingFiles = aggregatedEvidenceContext.Files
            .Where(file =>
                !string.Equals(file.Path, aggregatedEvidenceContext.LikelyMainFlowFile, StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(file.ObservationSummary))
            .Select(file => file.Path)
            .Take(2)
            .ToArray();

        var targetFiles = unreadSupportingFiles.Length > 0
            ? string.Join(", ", unreadSupportingFiles)
            : string.Join(", ", aggregatedEvidenceContext.Files
                .Where(file => string.IsNullOrWhiteSpace(file.ObservationSummary))
                .Select(file => file.Path)
                .Take(2));

        return $"Multiple meaningful source files appear relevant. Do not finalize yet. Inspect one more supporting file before answering, preferably from: {targetFiles}. For feature tracing, prioritize controller/service/model/frontend files that are closest to the requested feature intent. Then synthesize across the likely main flow file and supporting files, separating observed facts from inferred recommendations.";
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
