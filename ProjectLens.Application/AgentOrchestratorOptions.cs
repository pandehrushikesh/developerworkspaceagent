namespace ProjectLens.Application;

public sealed record AgentOrchestratorOptions
{
    public int MaxIterations { get; init; } = 8;
}
