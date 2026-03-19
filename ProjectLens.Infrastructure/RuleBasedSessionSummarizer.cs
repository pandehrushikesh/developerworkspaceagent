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
        var builder = new StringBuilder();
        var findings = ExtractFindings(toolName, toolOutput, _evidenceQualityEvaluator);

        if (!string.IsNullOrWhiteSpace(sessionState.WorkingSummary))
        {
            builder.AppendLine(sessionState.WorkingSummary.Trim());
        }

        builder.AppendLine($"Latest {toolName}: {DescribeLatestEvidence(toolName, toolOutput, sessionState)}");

        if (findings.MainFlowFiles.Count > 0)
        {
            builder.AppendLine($"Likely main flow files: {string.Join(", ", findings.MainFlowFiles)}");
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

        var curatedVisitedFiles = _evidenceQualityEvaluator.SelectPathsForSessionMemory(sessionState.VisitedFiles, 8);
        if (curatedVisitedFiles.Count > 0)
        {
            builder.AppendLine(
                $"Visited files: {string.Join(", ", curatedVisitedFiles)}");
        }

        if (sessionState.RecentToolHistory.Count > 0)
        {
            builder.AppendLine(
                $"Recent tool history: {string.Join(" | ", sessionState.RecentToolHistory.TakeLast(4))}");
        }

        return TrimToLength(builder.ToString().Trim(), 1_600);
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

    private static SessionFindings ExtractFindings(
        string toolName,
        string toolOutput,
        IEvidenceQualityEvaluator evidenceQualityEvaluator)
    {
        var files = new List<string>();
        var symbols = new List<string>();
        var operations = new List<string>();
        var limitations = new List<string>();
        var foundLowValueEvidence = false;

        foreach (var rawLine in toolOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var line = rawLine.Trim();

            if (line.StartsWith("File: ", StringComparison.Ordinal))
            {
                var path = line["File: ".Length..];
                files.Add(path);
                foundLowValueEvidence |= evidenceQualityEvaluator.IsLowValuePath(path);
                continue;
            }

            if (line.StartsWith("Likely classes: ", StringComparison.Ordinal) ||
                line.StartsWith("Likely methods: ", StringComparison.Ordinal))
            {
                var values = line[(line.IndexOf(':') + 1)..]
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                symbols.AddRange(values.Where(value => !string.Equals(value, "None identified", StringComparison.Ordinal)));
                operations.AddRange(values.Where(value => !string.Equals(value, "None identified", StringComparison.Ordinal)));
                continue;
            }

            if (line.StartsWith("Control flow: ", StringComparison.Ordinal))
            {
                var values = line["Control flow: ".Length..]
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                operations.AddRange(values.Where(value => !string.Equals(value, "None identified", StringComparison.Ordinal)));
                continue;
            }

            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                if (TryExtractSearchMatchPath(line[2..], out var path))
                {
                    files.Add(path);
                    foundLowValueEvidence |= evidenceQualityEvaluator.IsLowValuePath(path);
                }

                operations.Add(CreateSnippet(line[2..]));
                continue;
            }

            if (line.StartsWith("Evidence basis: ", StringComparison.Ordinal))
            {
                limitations.Add(CreateSnippet(line["Evidence basis: ".Length..]));
                continue;
            }

            if (string.Equals(toolName, "search_files", StringComparison.OrdinalIgnoreCase) &&
                line.StartsWith("search_files query: ", StringComparison.Ordinal))
            {
                operations.Add(line["search_files query: ".Length..]);
            }
        }

        var rankedFiles = evidenceQualityEvaluator
            .RankMatches(
                files.Select(path => new EvidenceMatch(path, string.Empty)),
                userPrompt: null,
                maxResults: 4)
            .Select(match => match.Path)
            .ToArray();

        return new SessionFindings(
            rankedFiles,
            symbols.Distinct(StringComparer.Ordinal).Take(8).ToArray(),
            operations.Distinct(StringComparer.Ordinal).Take(8).ToArray(),
            limitations.Distinct(StringComparer.Ordinal).Take(3).ToArray(),
            foundLowValueEvidence);
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
        IReadOnlyCollection<string> ImportantSymbols,
        IReadOnlyCollection<string> ObservedOperations,
        IReadOnlyCollection<string> EvidenceLimitations,
        bool FoundLowValueEvidence);
}
