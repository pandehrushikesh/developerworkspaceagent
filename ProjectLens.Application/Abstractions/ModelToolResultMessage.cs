namespace ProjectLens.Application.Abstractions;

public sealed record ModelToolResultMessage(
    string CallId,
    string ToolName,
    string Output) : ModelConversationItem;
