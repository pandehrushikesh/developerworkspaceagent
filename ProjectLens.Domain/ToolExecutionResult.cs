namespace ProjectLens.Domain;

public sealed record ToolExecutionResult(
    string ToolName,
    bool Success,
    string? Output = null,
    string? ErrorMessage = null);
