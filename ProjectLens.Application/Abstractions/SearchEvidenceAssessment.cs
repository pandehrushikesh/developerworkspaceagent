namespace ProjectLens.Application.Abstractions;

public sealed record SearchEvidenceAssessment(
    IReadOnlyCollection<EvidenceMatch> RankedMatches,
    bool IsWeakEvidence,
    bool HasMeaningfulSourceMatch,
    string RecoveryGuidance);
