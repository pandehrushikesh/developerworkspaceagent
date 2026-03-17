namespace ProjectLens.Application.Abstractions;

public sealed record ModelTextMessage(
    string Role,
    string Content) : ModelConversationItem;
