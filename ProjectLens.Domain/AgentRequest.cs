namespace ProjectLens.Domain;

public sealed record AgentRequest(
    string UserPrompt,
    string WorkspacePath,
    IReadOnlyDictionary<string, string>? Context = null,
    IReadOnlyCollection<ToolDefinition>? AvailableTools = null);
