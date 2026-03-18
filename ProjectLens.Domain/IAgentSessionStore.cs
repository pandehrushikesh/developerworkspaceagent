namespace ProjectLens.Domain;

public interface IAgentSessionStore
{
    Task<AgentSessionState?> GetAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        AgentSessionState sessionState,
        CancellationToken cancellationToken = default);
}
