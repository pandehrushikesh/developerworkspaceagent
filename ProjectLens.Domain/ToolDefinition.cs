namespace ProjectLens.Domain;

public sealed record ToolDefinition(
    string Name,
    string Description,
    IReadOnlyDictionary<string, string>? Parameters = null);
