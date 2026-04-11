namespace ProjectLens.Application.Abstractions;

public sealed record ToolOutputAdapterResult(
    string Output,
    AggregatedEvidenceContext? AggregatedEvidenceContext);
