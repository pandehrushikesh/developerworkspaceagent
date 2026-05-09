namespace ProjectLens.Application.Abstractions;

public sealed record ModelToolCallMessage(ModelToolCall ToolCall) : ModelConversationItem;
