namespace ProjectLens.Domain;

public interface IAgentOrchestrator
{
    Task<AgentResponse> ProcessAsync(
        AgentRequest request,
        CancellationToken cancellationToken = default);
}
