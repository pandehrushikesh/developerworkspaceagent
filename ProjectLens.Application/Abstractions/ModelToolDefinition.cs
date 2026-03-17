namespace ProjectLens.Application.Abstractions;

public sealed record ModelToolDefinition(
    string Name,
    string Description,
    IReadOnlyDictionary<string, string>? Parameters = null);
