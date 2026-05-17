namespace ProjectLens.Api.Models;

public sealed record EvidenceAssessmentDto(
    bool IsSufficient,
    double CoverageScore,
    double ConfidenceScore,
    string Reason,
    IReadOnlyCollection<string> MissingAreas);
