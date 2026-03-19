using System.Text.RegularExpressions;
using ProjectLens.Application.Abstractions;

namespace ProjectLens.Infrastructure;

public sealed class RuleBasedEvidenceQualityEvaluator : IEvidenceQualityEvaluator
{
    private static readonly string[] LowValueSegments =
    [
        "bin",
        "obj",
        ".vs",
        "node_modules",
        "dist",
        "build",
        "packages"
    ];

    private static readonly HashSet<string> SourceExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".vb", ".fs", ".js", ".ts", ".tsx", ".jsx", ".py", ".java", ".kt", ".go", ".rs", ".cpp", ".c", ".h", ".hpp"
    };

    private static readonly HashSet<string> ProjectExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".csproj", ".vbproj", ".fsproj", ".sln", ".props", ".targets"
    };

    private static readonly HashSet<string> ConfigAndDocExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".json", ".yaml", ".yml", ".config", ".md", ".xml", ".toml"
    };

    private static readonly string[] HighSignalNames =
    [
        "program",
        "startup",
        "service",
        "controller",
        "handler",
        "manager",
        "orchestrator",
        "agent",
        "repository",
        "installer",
        "host",
        "app"
    ];

    public bool IsLowValuePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var normalizedPath = NormalizePath(path);
        var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        return segments.Any(segment => LowValueSegments.Contains(segment, StringComparer.OrdinalIgnoreCase));
    }

    public bool IsMeaningfulSourcePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || IsLowValuePath(path))
        {
            return false;
        }

        var normalizedPath = NormalizePath(path);
        var fileName = Path.GetFileName(normalizedPath);
        var extension = Path.GetExtension(fileName);
        return SourceExtensions.Contains(extension);
    }

    public int ScoreFile(
        string path,
        string? snippet = null,
        string? userPrompt = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return int.MinValue;
        }

        var normalizedPath = NormalizePath(path);
        var fileName = Path.GetFileName(normalizedPath);
        var extension = Path.GetExtension(fileName);
        var score = 0;

        if (SourceExtensions.Contains(extension))
        {
            score += 120;
        }
        else if (ProjectExtensions.Contains(extension) ||
            fileName.Equals("package.json", StringComparison.OrdinalIgnoreCase))
        {
            score += 100;
        }
        else if (ConfigAndDocExtensions.Contains(extension) ||
            fileName.Equals(".editorconfig", StringComparison.OrdinalIgnoreCase))
        {
            score += 65;
        }
        else
        {
            score += 15;
        }

        var lowerFileName = fileName.ToLowerInvariant();
        foreach (var signal in HighSignalNames)
        {
            if (lowerFileName.Contains(signal, StringComparison.Ordinal))
            {
                score += 20;
            }
        }

        if (normalizedPath.Contains("/src/", StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.StartsWith("src/", StringComparison.OrdinalIgnoreCase))
        {
            score += 15;
        }

        if (normalizedPath.Contains("/test", StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.Contains("/tests", StringComparison.OrdinalIgnoreCase))
        {
            score -= 10;
        }

        if (lowerFileName.EndsWith(".g.cs", StringComparison.Ordinal) ||
            lowerFileName.Contains(".generated.", StringComparison.Ordinal) ||
            lowerFileName.Contains("assemblyinfo", StringComparison.Ordinal))
        {
            score -= 35;
        }

        if (IsLowValuePath(normalizedPath))
        {
            score -= 120;
        }

        score += ScorePromptRelevance(normalizedPath, snippet, userPrompt);
        return score;
    }

    public IReadOnlyCollection<EvidenceMatch> RankMatches(
        IEnumerable<EvidenceMatch> matches,
        string? userPrompt = null,
        int maxResults = 5)
    {
        ArgumentNullException.ThrowIfNull(matches);

        var ranked = matches
            .Select((match, index) => new RankedMatch(
                match,
                ScoreFile(match.Path, match.Snippet, userPrompt),
                IsLowValuePath(match.Path),
                index))
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.IsLowValuePath)
            .ThenBy(item => item.Match.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Match.LineNumber)
            .ThenBy(item => item.OriginalIndex)
            .ToArray();

        var hasHighValue = ranked.Any(item => !item.IsLowValuePath);
        var lowValueBudget = hasHighValue
            ? Math.Max(1, maxResults / 4)
            : maxResults;

        var selected = new List<EvidenceMatch>(maxResults);
        var lowValueCount = 0;

        foreach (var item in ranked)
        {
            if (selected.Count >= maxResults)
            {
                break;
            }

            if (hasHighValue && item.IsLowValuePath)
            {
                if (lowValueCount >= lowValueBudget)
                {
                    continue;
                }

                lowValueCount++;
            }

            selected.Add(item.Match);
        }

        return selected;
    }

    public SearchEvidenceAssessment AssessSearchEvidence(
        IEnumerable<EvidenceMatch> matches,
        string? userPrompt = null,
        int maxResults = 5)
    {
        ArgumentNullException.ThrowIfNull(matches);

        var rankedMatches = RankMatches(matches, userPrompt, maxResults).ToArray();
        var hasMeaningfulSourceMatch = rankedMatches.Any(match => IsMeaningfulSourcePath(match.Path));
        var hasMatches = rankedMatches.Length > 0;
        var allLowValue = hasMatches && rankedMatches.All(match => IsLowValuePath(match.Path));
        var allNonSource = hasMatches && rankedMatches.All(match => !IsMeaningfulSourcePath(match.Path));
        var isWeakEvidence = !hasMatches || allLowValue || allNonSource;

        return new SearchEvidenceAssessment(
            rankedMatches,
            isWeakEvidence,
            hasMeaningfulSourceMatch,
            BuildRecoveryGuidance(userPrompt, hasMatches, allLowValue, allNonSource));
    }

    public IReadOnlyCollection<string> SelectPathsForSessionMemory(
        IEnumerable<string> paths,
        int maxResults)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var normalizedPaths = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray();

        var hasHighValue = normalizedPaths.Any(path => !IsLowValuePath(path));
        var selected = new List<string>(maxResults);

        foreach (var path in normalizedPaths)
        {
            if (selected.Count >= maxResults)
            {
                break;
            }

            if (hasHighValue && IsLowValuePath(path))
            {
                continue;
            }

            selected.Add(path);
        }

        if (selected.Count > 0)
        {
            return selected;
        }

        return normalizedPaths.Take(maxResults).ToArray();
    }

    private static int ScorePromptRelevance(
        string normalizedPath,
        string? snippet,
        string? userPrompt)
    {
        if (string.IsNullOrWhiteSpace(userPrompt))
        {
            return 0;
        }

        var lowerPath = normalizedPath.ToLowerInvariant();
        var lowerSnippet = snippet?.ToLowerInvariant() ?? string.Empty;
        var score = 0;

        foreach (var token in Tokenize(userPrompt).Take(8))
        {
            if (lowerPath.Contains(token, StringComparison.Ordinal))
            {
                score += 16;
            }

            if (!string.IsNullOrWhiteSpace(lowerSnippet) &&
                lowerSnippet.Contains(token, StringComparison.Ordinal))
            {
                score += 10;
            }
        }

        return Math.Min(score, 50);
    }

    private static IEnumerable<string> Tokenize(string value)
    {
        return Regex.Matches(value.ToLowerInvariant(), "[a-z0-9]{3,}")
            .Select(match => match.Value)
            .Distinct(StringComparer.Ordinal);
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/').Trim();
    }

    private static string BuildRecoveryGuidance(
        string? userPrompt,
        bool hasMatches,
        bool allLowValue,
        bool allNonSource)
    {
        var suggestions = ExpandRelatedTerms(userPrompt);
        var nextStep = suggestions.Length > 0
            ? $"Try a broader related search such as: {string.Join(", ", suggestions)}."
            : "Try a broader related search using adjacent implementation terms.";

        if (!hasMatches)
        {
            return $"Weak evidence: the exact search did not return any grounded matches. {nextStep} If that still stays weak, inspect a likely main source file before answering.";
        }

        if (allLowValue)
        {
            return $"Weak evidence: the exact search returned only low-value or generated paths. {nextStep} If that still stays weak, inspect a likely main source file before answering.";
        }

        if (allNonSource)
        {
            return $"Weak evidence: the exact search returned only config, project, or other non-source matches, so that is not enough to explain application logic confidently. {nextStep} If that still stays weak, inspect a likely main source file before answering.";
        }

        return string.Empty;
    }

    private static string[] ExpandRelatedTerms(string? userPrompt)
    {
        if (string.IsNullOrWhiteSpace(userPrompt))
        {
            return Array.Empty<string>();
        }

        var prompt = userPrompt.ToLowerInvariant();
        var relatedTerms = new List<string>();

        AddRelatedTermsIfPresent(prompt, relatedTerms, "unzip", ["extract", "archive", "zip", "decompress", "unpack"]);
        AddRelatedTermsIfPresent(prompt, relatedTerms, "zip", ["archive", "compress", "extract", "unzip"]);
        AddRelatedTermsIfPresent(prompt, relatedTerms, "extract", ["archive", "zip", "unzip", "decompress"]);
        AddRelatedTermsIfPresent(prompt, relatedTerms, "archive", ["extract", "zip", "unzip", "decompress"]);

        return relatedTerms
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToArray();
    }

    private static void AddRelatedTermsIfPresent(
        string prompt,
        ICollection<string> relatedTerms,
        string token,
        IReadOnlyCollection<string> expansions)
    {
        if (!prompt.Contains(token, StringComparison.Ordinal))
        {
            return;
        }

        foreach (var expansion in expansions)
        {
            relatedTerms.Add(expansion);
        }
    }

    private sealed record RankedMatch(
        EvidenceMatch Match,
        int Score,
        bool IsLowValuePath,
        int OriginalIndex);
}
