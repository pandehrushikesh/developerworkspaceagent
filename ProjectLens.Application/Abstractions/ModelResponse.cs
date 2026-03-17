namespace ProjectLens.Application.Abstractions;

public sealed record ModelResponse(
    string? FinalAnswer = null,
    IReadOnlyCollection<ModelToolCall>? ToolCalls = null);
