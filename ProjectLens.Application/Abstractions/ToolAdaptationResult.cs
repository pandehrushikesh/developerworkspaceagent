namespace ProjectLens.Application.Abstractions;

public sealed record ToolAdaptationResult(
    string PromptText,
    IReadOnlyCollection<EvidenceItem> EvidenceItems);
