using System.Collections.Concurrent;
using ProjectLens.Domain;

namespace ProjectLens.Infrastructure;

public sealed class InMemoryAgentSessionStore : IAgentSessionStore
{
    private readonly ConcurrentDictionary<string, AgentSessionState> _sessions =
        new(StringComparer.OrdinalIgnoreCase);

    public Task<AgentSessionState?> GetAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _sessions.TryGetValue(sessionId, out var sessionState);
        return Task.FromResult(sessionState);
    }

    public Task SaveAsync(
        AgentSessionState sessionState,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionState);

        cancellationToken.ThrowIfCancellationRequested();
        _sessions[sessionState.SessionId] = sessionState with
        {
            VisitedFiles = sessionState.VisitedFiles.ToArray(),
            RecentToolHistory = sessionState.RecentToolHistory.ToArray()
        };

        return Task.CompletedTask;
    }
}
