namespace ProjectLens.Application.Abstractions;

public sealed record FinalAnswerDecision(
    bool WasHandled,
    bool ShouldFinalize,
    string? FinalAnswer,
    string? FollowUpPrompt);
