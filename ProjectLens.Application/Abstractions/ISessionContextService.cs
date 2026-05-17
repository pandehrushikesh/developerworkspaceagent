using ProjectLens.Domain;

namespace ProjectLens.Application.Abstractions;

public interface ISessionContextService
{
    Task<AgentSessionState?> LoadOrCreateAsync(
        AgentRequest request,
        CancellationToken cancellationToken = default);

    Task<AgentSessionState?> UpdateAsync(
        AgentSessionState? sessionState,
        string toolName,
        string toolOutputForModel,
        string? rawToolOutput,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists the agent's final answer so it can be injected as context in
    /// the next turn, allowing the model to interpret follow-up replies like
    /// "yes" or "go ahead" correctly.
    /// </summary>
    Task<AgentSessionState?> PersistFinalAnswerAsync(
        AgentSessionState? sessionState,
        string finalAnswer,
        CancellationToken cancellationToken = default);
}
