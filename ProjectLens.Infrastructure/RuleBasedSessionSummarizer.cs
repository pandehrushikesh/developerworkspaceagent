using System.Text;
using ProjectLens.Application.Abstractions;
using ProjectLens.Domain;

namespace ProjectLens.Infrastructure;

public sealed class RuleBasedSessionSummarizer : ISessionSummarizer
{
    public string UpdateSummary(
        AgentSessionState sessionState,
        string toolName,
        string toolOutput)
    {
        var builder = new StringBuilder();
        var findings = ExtractFindings(toolName, toolOutput);

        if (!string.IsNullOrWhiteSpace(sessionState.WorkingSummary))
        {
            builder.AppendLine(sessionState.WorkingSummary.Trim());
        }

        builder.AppendLine($"Latest {toolName}: {CreateSnippet(toolOutput)}");

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

        if (sessionState.VisitedFiles.Count > 0)
        {
            builder.AppendLine(
                $"Visited files: {string.Join(", ", sessionState.VisitedFiles.Take(8))}");
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

    private static SessionFindings ExtractFindings(string toolName, string toolOutput)
    {
        var files = new List<string>();
        var symbols = new List<string>();
        var operations = new List<string>();
        var limitations = new List<string>();

        foreach (var rawLine in toolOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var line = rawLine.Trim();

            if (line.StartsWith("File: ", StringComparison.Ordinal))
            {
                files.Add(line["File: ".Length..]);
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

        return new SessionFindings(
            files.Distinct(StringComparer.OrdinalIgnoreCase).Take(4).ToArray(),
            symbols.Distinct(StringComparer.Ordinal).Take(8).ToArray(),
            operations.Distinct(StringComparer.Ordinal).Take(8).ToArray(),
            limitations.Distinct(StringComparer.Ordinal).Take(3).ToArray());
    }

    private sealed record SessionFindings(
        IReadOnlyCollection<string> MainFlowFiles,
        IReadOnlyCollection<string> ImportantSymbols,
        IReadOnlyCollection<string> ObservedOperations,
        IReadOnlyCollection<string> EvidenceLimitations);
}
