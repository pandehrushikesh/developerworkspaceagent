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
        var now = DateTimeOffset.UtcNow;
        var createdAtUtc = sessionState.CreatedAtUtc == default ? now : sessionState.CreatedAtUtc;

        _sessions.AddOrUpdate(
            sessionState.SessionId,
            _ => Clone(sessionState, createdAtUtc, now),
            (_, existing) => Clone(
                sessionState,
                existing.CreatedAtUtc == default ? createdAtUtc : existing.CreatedAtUtc,
                now));

        return Task.CompletedTask;
    }

    private static AgentSessionState Clone(
        AgentSessionState sessionState,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc)
    {
        return sessionState with
        {
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = updatedAtUtc,
            VisitedFiles = sessionState.VisitedFiles.ToArray(),
            RecentToolHistory = sessionState.RecentToolHistory.ToArray()
        };
    }
}
