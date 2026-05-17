using System.Text.Json;
using ProjectLens.Application.Abstractions;
using ProjectLens.Domain;

namespace ProjectLens.Application;

public sealed class DefaultSessionContextService : ISessionContextService
{
    private const string ReadFileToolName = "read_file";
    private const string SearchFilesToolName = "search_files";
    private const string SessionIdContextKey = "sessionId";

    private readonly IEvidenceQualityEvaluator? _evidenceQualityEvaluator;
    private readonly IAgentSessionStore? _sessionStore;
    private readonly ISessionSummarizer? _sessionSummarizer;

    public DefaultSessionContextService(
        IAgentSessionStore? sessionStore = null,
        ISessionSummarizer? sessionSummarizer = null,
        IEvidenceQualityEvaluator? evidenceQualityEvaluator = null)
    {
        _sessionStore = sessionStore;
        _sessionSummarizer = sessionSummarizer;
        _evidenceQualityEvaluator = evidenceQualityEvaluator;
    }

    public async Task<AgentSessionState?> LoadOrCreateAsync(
        AgentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_sessionStore is null)
        {
            return null;
        }

        var sessionId = GetSessionId(request);
        var existingState = await _sessionStore.GetAsync(sessionId, cancellationToken);
        if (existingState is not null)
        {
            return existingState;
        }

        var sessionState = new AgentSessionState
        {
            SessionId = sessionId,
            WorkspacePath = request.WorkspacePath
        };

        await _sessionStore.SaveAsync(sessionState, cancellationToken);
        return sessionState;
    }

    public async Task<AgentSessionState?> UpdateAsync(
        AgentSessionState? sessionState,
        string toolName,
        string toolOutputForModel,
        string? rawToolOutput,
        CancellationToken cancellationToken = default)
    {
        if (sessionState is null || _sessionStore is null)
        {
            return sessionState;
        }

        var updatedVisitedFiles = UpdateRecentUniqueList(
            sessionState.VisitedFiles,
            ExtractVisitedFiles(toolName, rawToolOutput, _evidenceQualityEvaluator),
            20);

        if (_evidenceQualityEvaluator is not null)
        {
            updatedVisitedFiles = _evidenceQualityEvaluator.SelectPathsForSessionMemory(updatedVisitedFiles, 20);
        }

        var updatedHistory = sessionState.RecentToolHistory
            .Concat([CreateToolHistoryEntry(toolName, toolOutputForModel)])
            .TakeLast(8)
            .ToArray();

        var updatedState = sessionState with
        {
            VisitedFiles = updatedVisitedFiles,
            RecentToolHistory = updatedHistory
        };

        if (_sessionSummarizer is not null)
        {
            updatedState = updatedState with
            {
                WorkingSummary = _sessionSummarizer.UpdateSummary(updatedState, toolName, toolOutputForModel)
            };
        }

        await _sessionStore.SaveAsync(updatedState, cancellationToken);
        return updatedState;
    }

    private static IReadOnlyCollection<string> UpdateRecentUniqueList(
        IEnumerable<string> existingItems,
        IEnumerable<string> newItems,
        int maxEntries)
    {
        var recentItems = new List<string>();

        foreach (var item in existingItems.Concat(newItems))
        {
            if (string.IsNullOrWhiteSpace(item))
            {
                continue;
            }

            recentItems.RemoveAll(existing =>
                string.Equals(existing, item, StringComparison.OrdinalIgnoreCase));

            recentItems.Add(item);
        }

        if (recentItems.Count > maxEntries)
        {
            recentItems.RemoveRange(0, recentItems.Count - maxEntries);
        }

        return recentItems.ToArray();
    }

    private static IReadOnlyCollection<string> ExtractVisitedFiles(
        string toolName,
        string? rawToolOutput,
        IEvidenceQualityEvaluator? evidenceQualityEvaluator)
    {
        if (string.IsNullOrWhiteSpace(rawToolOutput))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(rawToolOutput);
            var root = document.RootElement;

            if (string.Equals(toolName, ReadFileToolName, StringComparison.OrdinalIgnoreCase))
            {
                var path = root.TryGetProperty("Path", out var pathElement)
                    ? pathElement.GetString()
                    : null;

                return string.IsNullOrWhiteSpace(path)
                    ? Array.Empty<string>()
                    : evidenceQualityEvaluator is null
                        ? [path]
                        : evidenceQualityEvaluator.SelectPathsForSessionMemory([path], 1);
            }

            if (string.Equals(toolName, SearchFilesToolName, StringComparison.OrdinalIgnoreCase) &&
                root.TryGetProperty("Matches", out var matchesElement))
            {
                var matches = matchesElement
                    .EnumerateArray()
                    .Select(match => new EvidenceMatch(
                        match.TryGetProperty("Path", out var pathElement) ? pathElement.GetString() ?? string.Empty : string.Empty,
                        match.TryGetProperty("Snippet", out var snippetElement) ? snippetElement.GetString() ?? string.Empty : string.Empty,
                        match.TryGetProperty("LineNumber", out var lineNumberElement) ? lineNumberElement.GetInt32() : 0))
                    .Where(match => !string.IsNullOrWhiteSpace(match.Path))
                    .ToArray();

                if (evidenceQualityEvaluator is null)
                {
                    return matches.Take(5).Select(match => match.Path).ToArray();
                }

                var query = root.TryGetProperty("Query", out var queryElement) ? queryElement.GetString() : null;
                var rankedPaths = evidenceQualityEvaluator
                    .RankMatches(matches, query, 5)
                    .Select(match => match.Path);

                return evidenceQualityEvaluator.SelectPathsForSessionMemory(rankedPaths, 5);
            }
        }
        catch
        {
            return Array.Empty<string>();
        }

        return Array.Empty<string>();
    }

    private static string CreateToolHistoryEntry(string toolName, string toolOutput)
    {
        var normalizedOutput = string.Join(
            ' ',
            toolOutput.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        const int maxLength = 180;
        if (normalizedOutput.Length > maxLength)
        {
            normalizedOutput = normalizedOutput[..(maxLength - 3)].TrimEnd() + "...";
        }

        return $"{toolName}: {normalizedOutput}";
    }

    public async Task<AgentSessionState?> PersistFinalAnswerAsync(
        AgentSessionState? sessionState,
        string finalAnswer,
        CancellationToken cancellationToken = default)
    {
        if (sessionState is null || _sessionStore is null || string.IsNullOrWhiteSpace(finalAnswer))
        {
            return sessionState;
        }

        const int maxLength = 800;
        var truncated = finalAnswer.Length > maxLength
            ? finalAnswer[..maxLength].TrimEnd() + "\n... (truncated)"
            : finalAnswer;

        var updatedState = sessionState with { LastAgentResponse = truncated };
        await _sessionStore.SaveAsync(updatedState, cancellationToken);
        return updatedState;
    }

    private static string GetSessionId(AgentRequest request)
    {
        if (request.Context is not null &&
            request.Context.TryGetValue(SessionIdContextKey, out var sessionId) &&
            !string.IsNullOrWhiteSpace(sessionId))
        {
            return sessionId.Trim();
        }

        return Path.GetFullPath(request.WorkspacePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToLowerInvariant();
    }
}
