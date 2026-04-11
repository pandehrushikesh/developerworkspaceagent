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
}
