using System.Text;
using ProjectLens.Application.Abstractions;
using ProjectLens.Domain;

namespace ProjectLens.Infrastructure;

public sealed class RuleBasedSessionSummarizer : ISessionSummarizer
{
    private readonly IEvidenceQualityEvaluator _evidenceQualityEvaluator;

    public RuleBasedSessionSummarizer(IEvidenceQualityEvaluator? evidenceQualityEvaluator = null)
    {
        _evidenceQualityEvaluator = evidenceQualityEvaluator ?? new RuleBasedEvidenceQualityEvaluator();
    }

    public string UpdateSummary(
        AgentSessionState sessionState,
        string toolName,
        string toolOutput)
    {
        var findings = ExtractFindings(toolName, toolOutput, _evidenceQualityEvaluator);
        var memory = CurateMemory(sessionState, findings);
        return RenderSummary(sessionState, toolName, toolOutput, findings, memory);
    }

    private static string CreateSnippet(string value)
    {
        var normalized = string.Join(
            ' ',
            value.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return TrimToLength(normalized, 280);
    }

    private static string TrimToLength(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[..(maxLength - 3)].TrimEnd() + "...";
    }

    private string DescribeLatestEvidence(
        string toolName,
        string toolOutput,
        AgentSessionState sessionState)
    {
        var findings = ExtractFindings(toolName, toolOutput, _evidenceQualityEvaluator);
        if (findings.FoundLowValueEvidence &&
            sessionState.VisitedFiles.Any(path => !_evidenceQualityEvaluator.IsLowValuePath(path)))
        {
            return $"low-value/generated artifact inspected; higher-signal files remain the preferred evidence base. Evidence basis: {CreateSnippet(toolOutput)}";
        }

        return CreateSnippet(toolOutput);
    }

    private string RenderSummary(
        AgentSessionState sessionState,
        string toolName,
        string toolOutput,
        SessionFindings findings,
        SummaryMemory memory)
    {
        var builder = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(sessionState.WorkingSummary))
        {
            builder.AppendLine(sessionState.WorkingSummary.Trim());
        }

        builder.AppendLine($"Latest {toolName}: {DescribeLatestEvidence(toolName, toolOutput, sessionState)}");
        AppendFindings(builder, findings);
        AppendMemory(builder, memory);

        return TrimToLength(builder.ToString().Trim(), 1_600);
    }

    private static SessionFindings ExtractFindings(
        string toolName,
        string toolOutput,
        IEvidenceQualityEvaluator evidenceQualityEvaluator)
    {
        var parsed = ParseToolOutput(toolName, toolOutput, evidenceQualityEvaluator);

        var rankedFiles = evidenceQualityEvaluator
            .RankMatches(
                parsed.Files.Select(path => new EvidenceMatch(path, string.Empty)),
                userPrompt: null,
                maxResults: 4)
            .Select(match => match.Path)
            .ToArray();

        if (!string.IsNullOrWhiteSpace(parsed.ExplicitMainFlowFile))
        {
            rankedFiles = rankedFiles
                .Where(path => !string.Equals(path, parsed.ExplicitMainFlowFile, StringComparison.OrdinalIgnoreCase))
                .Prepend(parsed.ExplicitMainFlowFile)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(4)
                .ToArray();
        }

        return new SessionFindings(
            rankedFiles,
            evidenceQualityEvaluator.SelectPathsForSessionMemory(parsed.SupportingFiles, 4),
            parsed.Symbols.Distinct(StringComparer.Ordinal).Take(8).ToArray(),
            parsed.Operations.Distinct(StringComparer.Ordinal).Take(8).ToArray(),
            parsed.Limitations.Distinct(StringComparer.Ordinal).Take(3).ToArray(),
            parsed.FoundLowValueEvidence,
            parsed.HasProvisionalMainFlow);
    }

    private static ParsedFindings ParseToolOutput(
        string toolName,
        string toolOutput,
        IEvidenceQualityEvaluator evidenceQualityEvaluator)
    {
        var parsed = new ParsedFindings();

        foreach (var rawLine in toolOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            ParseLine(parsed, toolName, rawLine.Trim(), evidenceQualityEvaluator);
        }

        return parsed;
    }

    private static void ParseLine(
        ParsedFindings parsed,
        string toolName,
        string line,
        IEvidenceQualityEvaluator evidenceQualityEvaluator)
    {
        if (line.StartsWith("File: ", StringComparison.Ordinal))
        {
            AddFile(parsed, line["File: ".Length..], evidenceQualityEvaluator);
            return;
        }

        if (line.StartsWith("Likely main flow file: ", StringComparison.Ordinal))
        {
            var path = line["Likely main flow file: ".Length..].Trim();
            parsed.ExplicitMainFlowFile = path;
            AddFile(parsed, path, evidenceQualityEvaluator);
            return;
        }

        if (line.StartsWith("Feature flow confidence: ", StringComparison.Ordinal))
        {
            var featureFlowConfidence = line["Feature flow confidence: ".Length..].Trim();
            parsed.HasProvisionalMainFlow |= featureFlowConfidence.Equals("provisional", StringComparison.OrdinalIgnoreCase);
            return;
        }

        if (line.StartsWith("Supporting file: ", StringComparison.Ordinal))
        {
            var path = line["Supporting file: ".Length..]
                .Split('|', 2, StringSplitOptions.TrimEntries)[0]
                .Trim();
            if (!string.IsNullOrWhiteSpace(path))
            {
                parsed.SupportingFiles.Add(path);
                AddFile(parsed, path, evidenceQualityEvaluator);
            }

            return;
        }

        if (line.StartsWith("Likely classes: ", StringComparison.Ordinal) ||
            line.StartsWith("Likely methods: ", StringComparison.Ordinal))
        {
            var values = line[(line.IndexOf(':') + 1)..]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            parsed.Symbols.AddRange(values.Where(value => !string.Equals(value, "None identified", StringComparison.Ordinal)));
            parsed.Operations.AddRange(values.Where(value => !string.Equals(value, "None identified", StringComparison.Ordinal)));
            return;
        }

        if (line.StartsWith("Control flow: ", StringComparison.Ordinal))
        {
            var values = line["Control flow: ".Length..]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            parsed.Operations.AddRange(values.Where(value => !string.Equals(value, "None identified", StringComparison.Ordinal)));
            return;
        }

        if (line.StartsWith("- ", StringComparison.Ordinal))
        {
            if (TryExtractSearchMatchPath(line[2..], out var path))
            {
                AddFile(parsed, path, evidenceQualityEvaluator);
            }

            parsed.Operations.Add(CreateSnippet(line[2..]));
            return;
        }

        if (line.StartsWith("Evidence basis: ", StringComparison.Ordinal))
        {
            parsed.Limitations.Add(CreateSnippet(line["Evidence basis: ".Length..]));
            return;
        }

        if (line.StartsWith("Observed file summary: ", StringComparison.Ordinal))
        {
            parsed.Operations.Add(CreateSnippet(line));
            return;
        }

        if (line.StartsWith("Aggregation limitation: ", StringComparison.Ordinal))
        {
            var limitation = CreateSnippet(line["Aggregation limitation: ".Length..]);
            parsed.Limitations.Add(limitation);
            parsed.HasProvisionalMainFlow |= limitation.Contains("provisional", StringComparison.OrdinalIgnoreCase);
            return;
        }

        if (string.Equals(toolName, "search_files", StringComparison.OrdinalIgnoreCase) &&
            line.StartsWith("search_files query: ", StringComparison.Ordinal))
        {
            parsed.Operations.Add(line["search_files query: ".Length..]);
        }
    }

    private static void AddFile(
        ParsedFindings parsed,
        string path,
        IEvidenceQualityEvaluator evidenceQualityEvaluator)
    {
        parsed.Files.Add(path);
        parsed.FoundLowValueEvidence |= evidenceQualityEvaluator.IsLowValuePath(path);
    }

    private SummaryMemory CurateMemory(
        AgentSessionState sessionState,
        SessionFindings findings)
    {
        return new SummaryMemory(
            _evidenceQualityEvaluator.SelectPathsForSessionMemory(sessionState.VisitedFiles, 8),
            sessionState.RecentToolHistory.TakeLast(4).ToArray(),
            findings.HasProvisionalMainFlow);
    }

    private static void AppendFindings(
        StringBuilder builder,
        SessionFindings findings)
    {
        if (findings.MainFlowFiles.Count > 0)
        {
            builder.AppendLine(findings.HasProvisionalMainFlow
                ? $"Feature flow candidates: {string.Join(", ", findings.MainFlowFiles)}"
                : $"Likely main flow files: {string.Join(", ", findings.MainFlowFiles)}");
        }

        if (findings.SupportingFiles.Count > 0)
        {
            builder.AppendLine($"Supporting files: {string.Join(", ", findings.SupportingFiles)}");
        }

        if (findings.ImportantSymbols.Count > 0)
        {
            builder.AppendLine($"Important symbols: {string.Join(", ", findings.ImportantSymbols)}");
        }

        if (findings.ObservedOperations.Count > 0)
        {
            builder.AppendLine($"Observed operations: {string.Join(", ", findings.ObservedOperations)}");
        }

        if (findings.EvidenceLimitations.Count > 0)
        {
            builder.AppendLine($"Evidence limitations: {string.Join(" | ", findings.EvidenceLimitations)}");
        }
    }

    private static void AppendMemory(
        StringBuilder builder,
        SummaryMemory memory)
    {
        if (memory.HasProvisionalMainFlow)
        {
            builder.AppendLine("Current feature-flow understanding is provisional and may shift as more supporting files are read.");
        }

        if (memory.VisitedFiles.Count > 0)
        {
            builder.AppendLine($"Visited files: {string.Join(", ", memory.VisitedFiles)}");
        }

        if (memory.RecentHistory.Count > 0)
        {
            builder.AppendLine($"Recent tool history: {string.Join(" | ", memory.RecentHistory)}");
        }
    }

    private static bool TryExtractSearchMatchPath(string value, out string path)
    {
        var separatorIndex = value.IndexOf(':');
        if (separatorIndex <= 0)
        {
            path = string.Empty;
            return false;
        }

        path = value[..separatorIndex].Trim();
        return !string.IsNullOrWhiteSpace(path);
    }

    private sealed record SessionFindings(
        IReadOnlyCollection<string> MainFlowFiles,
        IReadOnlyCollection<string> SupportingFiles,
        IReadOnlyCollection<string> ImportantSymbols,
        IReadOnlyCollection<string> ObservedOperations,
        IReadOnlyCollection<string> EvidenceLimitations,
        bool FoundLowValueEvidence,
        bool HasProvisionalMainFlow);

    private sealed class ParsedFindings
    {
        public List<string> Files { get; } = new();

        public List<string> SupportingFiles { get; } = new();

        public List<string> Symbols { get; } = new();

        public List<string> Operations { get; } = new();

        public List<string> Limitations { get; } = new();

        public bool FoundLowValueEvidence { get; set; }

        public bool HasProvisionalMainFlow { get; set; }

        public string? ExplicitMainFlowFile { get; set; }
    }

    private sealed record SummaryMemory(
        IReadOnlyCollection<string> VisitedFiles,
        IReadOnlyCollection<string> RecentHistory,
        bool HasProvisionalMainFlow);
}
