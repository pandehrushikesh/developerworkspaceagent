namespace ProjectLens.Domain;

public sealed record AgentResponse(
    string Output,
    IReadOnlyCollection<AgentExecutionStep>? ExecutionSteps = null,
    IReadOnlyCollection<ToolExecutionResult>? ToolResults = null,
    bool Success = true,
    string? ErrorMessage = null);
