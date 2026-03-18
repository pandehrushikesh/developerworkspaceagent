namespace ProjectLens.Application.Abstractions;

public sealed record ModelRequest(
    string Instructions,
    IReadOnlyCollection<ModelConversationItem> Conversation,
    IReadOnlyCollection<ModelToolDefinition> AvailableTools,
    string? PreviousResponseId = null);
