using System.Text.RegularExpressions;
using ProjectLens.Application.Abstractions;
using ProjectLens.Domain;

namespace ProjectLens.Application;

public sealed class RuleBasedPromptClarifier : IPromptClarifier
{
    private static readonly HashSet<string> GenericPromptTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "this", "that", "those", "these", "it", "flow", "logic", "works", "work", "trace", "explain",
        "refactor", "how", "does", "what", "which", "where", "across", "codebase", "repository", "feature", "files",
        "appear", "drive", "now"
    };

    private static readonly string[] AmbiguousPhrases =
    [
        "explain the flow",
        "trace the logic",
        "trace the flow",
        "refactor that",
        "refactor this",
        "how does this work",
        "how does it work",
        "explain this",
        "explain that"
    ];

    private static readonly string[] FollowUpSignals =
    [
        "this",
        "that",
        "it",
        "the flow",
        "the logic",
        "that flow",
        "that logic",
        "that feature"
    ];

    public PromptClarification? GetClarification(
        string userPrompt,
        AgentSessionState? sessionState)
    {
        if (string.IsNullOrWhiteSpace(userPrompt))
        {
            return null;
        }

        var normalizedPrompt = userPrompt.Trim();
        if (IsClearlyAnchoredPrompt(normalizedPrompt))
        {
            return null;
        }

        var sessionContext = ExtractSessionContext(sessionState);
        if (sessionContext.HasStrongSingleFlow && IsFollowUpPrompt(normalizedPrompt))
        {
            return null;
        }

        var isAmbiguous = HasExplicitAmbiguitySignal(normalizedPrompt)
            || HasTooFewMeaningfulTokens(normalizedPrompt)
            || (IsFollowUpPrompt(normalizedPrompt) && sessionContext.CandidateOptions.Count != 1);

        if (!isAmbiguous)
        {
            return null;
        }

        var question = BuildQuestion(sessionContext.CandidateOptions);
        return new PromptClarification(
            question,
            sessionContext.CandidateOptions,
            sessionContext.CandidateOptions.Count > 0);
    }

    private static bool IsClearlyAnchoredPrompt(string userPrompt)
    {
        if (string.IsNullOrWhiteSpace(userPrompt))
        {
            return false;
        }

        if (userPrompt.Contains("architecture", StringComparison.OrdinalIgnoreCase) ||
            userPrompt.Contains("repository", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (Regex.IsMatch(userPrompt, @"\b[A-Z][a-z0-9_]+[A-Z][A-Za-z0-9_]*\b") ||
            Regex.IsMatch(userPrompt, @"[\w\-/\\]+\.[A-Za-z0-9]+"))
        {
            return true;
        }

        var meaningfulTokens = Tokenize(userPrompt)
            .Where(token => !GenericPromptTokens.Contains(token))
            .Take(3)
            .ToArray();

        return meaningfulTokens.Length >= 2;
    }

    private static bool HasExplicitAmbiguitySignal(string userPrompt)
    {
        var normalizedPrompt = userPrompt.ToLowerInvariant();
        return AmbiguousPhrases.Any(phrase => normalizedPrompt.Contains(phrase, StringComparison.Ordinal))
            || (IsFollowUpPrompt(normalizedPrompt) && !IsClearlyAnchoredPrompt(userPrompt));
    }

    private static bool HasTooFewMeaningfulTokens(string userPrompt)
    {
        var meaningfulTokens = Tokenize(userPrompt)
            .Where(token => !GenericPromptTokens.Contains(token))
            .Take(2)
            .ToArray();

        return meaningfulTokens.Length == 0;
    }

    private static bool IsFollowUpPrompt(string userPrompt)
    {
        var normalizedPrompt = userPrompt.ToLowerInvariant();
        return FollowUpSignals.Any(signal =>
            Regex.IsMatch(
                normalizedPrompt,
                $@"\b{Regex.Escape(signal)}\b",
                RegexOptions.CultureInvariant));
    }

    private static SessionClarificationContext ExtractSessionContext(AgentSessionState? sessionState)
    {
        if (sessionState is null)
        {
            return new SessionClarificationContext(Array.Empty<string>(), false);
        }

        var candidates = new List<string>();
        var hasProvisionalContext = false;
        var strongMainFlowLabels = new List<string>();

        foreach (var line in sessionState.WorkingSummary.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith("Feature flow candidates: ", StringComparison.Ordinal))
            {
                hasProvisionalContext = true;
                candidates.AddRange(ExtractLabelsFromPathList(line["Feature flow candidates: ".Length..]));
                continue;
            }

            if (line.StartsWith("Likely main flow files: ", StringComparison.Ordinal))
            {
                strongMainFlowLabels.AddRange(ExtractLabelsFromPathList(line["Likely main flow files: ".Length..]));
                continue;
            }

            if (line.StartsWith("Supporting files: ", StringComparison.Ordinal))
            {
                candidates.AddRange(ExtractLabelsFromPathList(line["Supporting files: ".Length..]));
                continue;
            }

            if (line.Contains("Feature flow confidence: provisional", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Current feature-flow understanding is provisional", StringComparison.OrdinalIgnoreCase))
            {
                hasProvisionalContext = true;
            }
        }

        candidates.InsertRange(0, ExtractLabelsFromSessionSummary(sessionState.WorkingSummary));

        var recentSearches = sessionState.RecentToolHistory
            .TakeLast(4)
            .Where(entry => entry.StartsWith("search_files:", StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry[(entry.IndexOf(':') + 1)..].Trim())
            .SelectMany(ExtractLabelsFromSearchQuery)
            .ToArray();

        candidates.AddRange(recentSearches);
        candidates.AddRange(sessionState.VisitedFiles.TakeLast(4).SelectMany(path => ExtractLabelsFromPathList(path)));

        var selectedCandidates = strongMainFlowLabels
            .Concat(candidates)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToArray();

        var hasStrongSingleFlow = !hasProvisionalContext && strongMainFlowLabels
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .Count() == 1;

        return new SessionClarificationContext(selectedCandidates, hasStrongSingleFlow);
    }

    private static IReadOnlyCollection<string> ExtractLabelsFromSearchQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<string>();
        }

        var labels = Tokenize(query)
            .Where(token => !GenericPromptTokens.Contains(token))
            .Select(TokenToFlowLabel)
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToArray();

        return labels;
    }

    private static IReadOnlyCollection<string> ExtractLabelsFromSessionSummary(string summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            return Array.Empty<string>();
        }

        var labels = new List<string>();
        var normalizedSummary = summary.ToLowerInvariant();

        if (ContainsAny(normalizedSummary, "blog", "blogs", "post", "article"))
        {
            labels.Add("blog flow");
        }

        if (ContainsAny(normalizedSummary, "auth", "login", "token", "session"))
        {
            labels.Add("authentication flow");
        }

        if (ContainsAny(normalizedSummary, "install", "installer", "setup"))
        {
            labels.Add("installer flow");
        }

        if (ContainsAny(normalizedSummary, "archive", "extract", "unzip", "zip"))
        {
            labels.Add("archive flow");
        }

        if (ContainsAny(normalizedSummary, "agent", "orchestrator"))
        {
            labels.Add("agent orchestration flow");
        }

        return labels
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToArray();
    }

    private static IReadOnlyCollection<string> ExtractLabelsFromPathList(string rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return Array.Empty<string>();
        }

        return rawValue
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(PathToFlowLabel)
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToArray();
    }

    private static string PathToFlowLabel(string path)
    {
        var normalizedPath = path.Replace('\\', '/').ToLowerInvariant();
        var tokens = ExtractWordTokens(normalizedPath);

        if (ContainsAnyToken(tokens, "blog", "blogs", "post", "article"))
        {
            return "blog flow";
        }

        if (ContainsAnyToken(tokens, "auth", "login", "token", "session", "user"))
        {
            return "authentication flow";
        }

        if (ContainsAnyToken(tokens, "install", "installer", "setup"))
        {
            return "installer flow";
        }

        if (ContainsAnyToken(tokens, "archive", "extract", "unzip", "zip"))
        {
            return "archive flow";
        }

        if (ContainsAnyToken(tokens, "agent", "orchestrator"))
        {
            return "agent orchestration flow";
        }

        var fileName = Path.GetFileNameWithoutExtension(normalizedPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return string.Empty;
        }

        var simplified = Regex.Replace(fileName, "(controller|service|manager|handler|models?|repository|app|program|startup)$", string.Empty, RegexOptions.IgnoreCase);
        simplified = Regex.Replace(simplified, "([a-z])([A-Z])", "$1 $2");
        simplified = simplified.Replace('-', ' ').Replace('_', ' ').Trim();

        return string.IsNullOrWhiteSpace(simplified)
            ? string.Empty
            : $"{simplified.ToLowerInvariant()} flow";
    }

    private static string TokenToFlowLabel(string token)
    {
        if (ContainsAny(token, "blog", "post", "article"))
        {
            return "blog flow";
        }

        if (ContainsAny(token, "auth", "login", "token", "session", "user"))
        {
            return "authentication flow";
        }

        if (ContainsAny(token, "install", "installer"))
        {
            return "installer flow";
        }

        if (ContainsAny(token, "archive", "extract", "unzip", "zip"))
        {
            return "archive flow";
        }

        return token.Length < 3
            ? string.Empty
            : $"{token} flow";
    }

    private static string BuildQuestion(IReadOnlyCollection<string> candidateOptions)
    {
        var options = candidateOptions
            .Where(option => !string.IsNullOrWhiteSpace(option))
            .Take(3)
            .ToArray();

        return options.Length switch
        {
            0 => "Which feature, file, or flow do you want me to inspect?",
            1 => $"Do you mean the {options[0]}, or another feature?",
            2 => $"Do you mean the {options[0]} or the {options[1]}?",
            _ => $"Do you mean the {options[0]}, the {options[1]}, or another feature?"
        };
    }

    private static IEnumerable<string> Tokenize(string value)
    {
        return Regex.Matches(value.ToLowerInvariant(), "[a-z0-9]{3,}")
            .Select(match => match.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static bool ContainsAny(string value, params string[] tokens)
    {
        return tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsAnyToken(IReadOnlyCollection<string> tokens, params string[] candidates)
    {
        return tokens.Any(token =>
            candidates.Any(candidate =>
                token.Equals(candidate, StringComparison.OrdinalIgnoreCase) ||
                token.StartsWith(candidate, StringComparison.OrdinalIgnoreCase)));
    }

    private static IReadOnlyCollection<string> ExtractWordTokens(string value)
    {
        return Regex.Matches(value, "[a-z0-9]+")
            .Select(match => match.Value)
            .ToArray();
    }

    private sealed record SessionClarificationContext(
        IReadOnlyCollection<string> CandidateOptions,
        bool HasStrongSingleFlow);
}
