namespace ProjectLens.Application.Abstractions;

public sealed record AggregatedEvidenceFile(
    string Path,
    string SelectionReason,
    string ObservationSummary);
