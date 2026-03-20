using System.Text.RegularExpressions;
using ProjectLens.Application.Abstractions;
using ProjectLens.Domain;

namespace ProjectLens.Application;

public sealed class RuleBasedPromptClarifier : IPromptClarifier
{
    private static readonly HashSet<string> StructuralTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "controller", "service", "manager", "handler", "model", "models", "repository", "entity",
        "app", "program", "startup", "host", "api", "src", "client", "server"
    };

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
        var normalizedPath = path.Replace('\\', '/');
        var fileName = Path.GetFileNameWithoutExtension(normalizedPath);
        var fileTokens = ExtractLabelTokens(fileName);
        if (fileTokens.Length > 0)
        {
            return $"{string.Join(" ", fileTokens)} flow";
        }

        var directoryTokens = normalizedPath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Reverse()
            .Skip(1)
            .SelectMany(ExtractLabelTokens)
            .Take(2)
            .ToArray();

        return directoryTokens.Length == 0
            ? string.Empty
            : $"{string.Join(" ", directoryTokens)} flow";
    }

    private static string TokenToFlowLabel(string token)
    {
        var labelTokens = ExtractLabelTokens(token).Take(2).ToArray();
        return labelTokens.Length == 0
            ? string.Empty
            : $"{string.Join(" ", labelTokens)} flow";
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

    private static IReadOnlyCollection<string> ExtractWordTokens(string value)
    {
        return Regex.Matches(value.ToLowerInvariant(), "[a-z0-9]+")
            .Select(match => match.Value)
            .ToArray();
    }

    private static string[] ExtractLabelTokens(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        var simplified = Regex.Replace(
            value,
            "(controller|service|manager|handler|models?|repository|entity|app|program|startup|host|api|client|server)$",
            string.Empty,
            RegexOptions.IgnoreCase);
        var expanded = Regex.Replace(simplified, "([a-z])([A-Z])", "$1 $2");
        return ExtractWordTokens(expanded)
            .Where(token => token.Length >= 3)
            .Where(token => !GenericPromptTokens.Contains(token))
            .Where(token => !StructuralTokens.Contains(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToArray();
    }

    private sealed record SessionClarificationContext(
        IReadOnlyCollection<string> CandidateOptions,
        bool HasStrongSingleFlow);
}
