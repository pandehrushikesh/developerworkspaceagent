namespace ProjectLens.Domain;

public sealed record AgentExecutionStep(
    string Description,
    bool Success = true);
