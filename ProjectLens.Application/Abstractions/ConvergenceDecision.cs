namespace ProjectLens.Application.Abstractions;

public sealed record ConvergenceDecision(
    ConvergenceDecisionType DecisionType,
    string Reason,
    string? Guidance);
