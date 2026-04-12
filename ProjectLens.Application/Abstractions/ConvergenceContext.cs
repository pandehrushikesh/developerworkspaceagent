namespace ProjectLens.Application.Abstractions;

public sealed record ConvergenceContext(
    int IterationNumber = 0,
    int DuplicateToolCallCount = 0,
    int ConsecutiveNoProgressCount = 0,
    bool HadToolFailure = false,
    int? NewEvidenceCount = null);
