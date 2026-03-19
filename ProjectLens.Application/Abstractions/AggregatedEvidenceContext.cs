namespace ProjectLens.Application.Abstractions;

public sealed record AggregatedEvidenceContext(
    bool IsFeatureTrace,
    bool IsProvisional,
    string? LikelyMainFlowFile,
    IReadOnlyCollection<AggregatedEvidenceFile> Files,
    IReadOnlyCollection<string> EvidenceLimitations);
