namespace ProjectLens.Application.Abstractions;

public sealed record PromptClarification(
    string Question,
    IReadOnlyCollection<string> CandidateOptions,
    bool UsesSessionContext);
