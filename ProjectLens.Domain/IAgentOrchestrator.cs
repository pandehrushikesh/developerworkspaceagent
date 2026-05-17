namespace ProjectLens.Domain;

public interface IAgentOrchestrator
{
    Task<AgentResponse> ProcessAsync(
        AgentRequest request,
        CancellationToken cancellationToken = default);

    Task<AgentResponse> ProcessAsync(
        AgentRequest request,
        IProgress<AgentProgressEvent> progress,
        CancellationToken cancellationToken = default);
}
