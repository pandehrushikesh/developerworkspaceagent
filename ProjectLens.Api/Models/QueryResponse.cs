namespace ProjectLens.Api.Models;

public sealed record QueryResponse(
    bool Success,
    string? Output,
    string? ErrorMessage,
    IReadOnlyCollection<ExecutionStepDto> ExecutionSteps,
    IReadOnlyCollection<ToolResultDto> ToolResults);
