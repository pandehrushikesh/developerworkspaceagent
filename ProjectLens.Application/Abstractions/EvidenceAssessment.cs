namespace ProjectLens.Application.Abstractions;

public sealed record EvidenceAssessment(
    bool IsSufficient,
    double CoverageScore,
    double ConfidenceScore,
    string Reason,
    IReadOnlyCollection<string> MissingAreas);
