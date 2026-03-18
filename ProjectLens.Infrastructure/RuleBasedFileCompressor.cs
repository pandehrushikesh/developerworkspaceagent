using System.Text;
using System.Text.RegularExpressions;
using ProjectLens.Application.Abstractions;

namespace ProjectLens.Infrastructure;

public sealed class RuleBasedFileCompressor : IFileCompressor
{
    private static readonly HashSet<string> ControlFlowKeywords = new(StringComparer.Ordinal)
    {
        "if",
        "else",
        "switch",
        "case",
        "for",
        "foreach",
        "while",
        "return",
        "throw",
        "try",
        "catch",
        "await",
        "break",
        "continue"
    };

    private static readonly Regex MethodRegex = new(
        @"\b(?:public|private|internal|protected)\s+(?:static\s+)?(?:async\s+)?(?:[\w<>\[\],?]+\s+)+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(",
        RegexOptions.Compiled);
    private static readonly Regex SymbolRegex = new(
        @"\b(class|interface|record|struct|enum)\s+([A-Za-z_][A-Za-z0-9_]*)|\b([A-Za-z_][A-Za-z0-9_]*)\s*\(",
        RegexOptions.Compiled);

    public string Compress(
        string filePath,
        string content,
        string? focusHint = null)
    {
        var normalizedLines = content
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

        var preview = CreatePreview(normalizedLines);
        var classNames = ExtractClassNames(normalizedLines);
        var methodNames = ExtractMethodNames(normalizedLines);
        var controlFlowLines = ExtractControlFlowLines(normalizedLines);
        var relevantSnippets = ExtractRelevantSnippets(normalizedLines, focusHint, classNames, methodNames);

        var builder = new StringBuilder();
        builder.AppendLine($"File: {filePath}");
        builder.AppendLine($"Preview: {preview}");
        builder.AppendLine($"Likely classes: {FormatSection(classNames)}");
        builder.AppendLine($"Likely methods: {FormatSection(methodNames)}");
        builder.AppendLine($"Control flow: {FormatSection(controlFlowLines)}");

        if (relevantSnippets.Count > 0)
        {
            builder.AppendLine("Relevant snippets:");
            foreach (var snippet in relevantSnippets)
            {
                builder.AppendLine($"- {snippet}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string CreatePreview(IReadOnlyCollection<string> lines)
    {
        var preview = string.Join(" ", lines.Take(3));
        return TrimToLength(preview, 220);
    }

    private static IReadOnlyCollection<string> ExtractClassNames(IReadOnlyCollection<string> lines)
    {
        return lines
            .SelectMany(line => SymbolRegex.Matches(line).Select(match => match.Groups[2].Success ? match.Groups[2].Value : string.Empty))
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Distinct(StringComparer.Ordinal)
            .Take(6)
            .ToArray();
    }

    private static IReadOnlyCollection<string> ExtractMethodNames(IReadOnlyCollection<string> lines)
    {
        return lines
            .SelectMany(line => MethodRegex.Matches(line).Select(match => match.Groups["name"].Value))
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Distinct(StringComparer.Ordinal)
            .Take(8)
            .ToArray();
    }

    private static IReadOnlyCollection<string> ExtractControlFlowLines(IReadOnlyCollection<string> lines)
    {
        return lines
            .Where(line => ControlFlowKeywords.Any(keyword => line.Contains(keyword, StringComparison.Ordinal)))
            .Select(line => TrimToLength(line, 140))
            .Distinct(StringComparer.Ordinal)
            .Take(4)
            .ToArray();
    }

    private static IReadOnlyCollection<string> ExtractRelevantSnippets(
        IReadOnlyCollection<string> lines,
        string? focusHint,
        IReadOnlyCollection<string> classNames,
        IReadOnlyCollection<string> methodNames)
    {
        var focusTerms = (focusHint ?? string.Empty)
            .Split([' ', '\t', '\r', '\n', ',', '.', ':', ';', '/', '\\', '(', ')', '[', ']'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(term => term.Length >= 4)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var snippets = lines
            .Where(line =>
                focusTerms.Any(term => line.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                classNames.Any(symbol => line.Contains(symbol, StringComparison.Ordinal)) ||
                methodNames.Any(symbol => line.Contains(symbol, StringComparison.Ordinal)) ||
                ControlFlowKeywords.Any(keyword => line.Contains(keyword, StringComparison.Ordinal)))
            .Select(line => TrimToLength(line, 180))
            .Distinct(StringComparer.Ordinal)
            .Take(5)
            .ToList();

        if (snippets.Count == 0)
        {
            snippets.AddRange(lines.Take(2).Select(line => TrimToLength(line, 180)));
        }

        return snippets;
    }

    private static string FormatSection(IReadOnlyCollection<string> items)
    {
        return items.Count == 0
            ? "None identified"
            : string.Join(", ", items);
    }

    private static string TrimToLength(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[..(maxLength - 3)].TrimEnd() + "...";
    }
}
