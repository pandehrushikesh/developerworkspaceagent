namespace ProjectLens.Domain;

public abstract record AgentProgressEvent;

public sealed record StepProgressEvent(string Description, bool Success) : AgentProgressEvent;

public sealed record ToolResultProgressEvent(
    string ToolName,
    bool Success,
    string? ErrorMessage) : AgentProgressEvent;

public sealed record AnswerProgressEvent(string Text) : AgentProgressEvent;
