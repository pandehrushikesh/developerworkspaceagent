namespace ProjectLens.Application.Abstractions;

public sealed record ModelToolCall(
    string CallId,
    string ToolName,
    IReadOnlyDictionary<string, string> Arguments);
