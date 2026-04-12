using ProjectLens.Application.Abstractions;

namespace ProjectLens.Application;

public sealed class RuleBasedConvergencePolicy : IConvergencePolicy
{
    public ConvergenceDecision Evaluate(
        EvidenceAssessment assessment,
        IReadOnlyCollection<EvidenceItem> evidenceItems,
        ConvergenceContext context)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        ArgumentNullException.ThrowIfNull(evidenceItems);
        ArgumentNullException.ThrowIfNull(context);

        var meaningfulEvidence = evidenceItems
            .Where(item => !string.IsNullOrWhiteSpace(item.Content))
            .ToArray();
        var hasEvidence = meaningfulEvidence.Length > 0;
        var hasDirectEvidence = meaningfulEvidence.Any(item => item.Kind == EvidenceKind.DirectSnippet);
        var onlySearchHits = hasEvidence && meaningfulEvidence.All(item => item.Kind == EvidenceKind.SearchHit);
        var partialRatio = hasEvidence
            ? (double)meaningfulEvidence.Count(item => item.IsPartial) / meaningfulEvidence.Length
            : 0;
        var directSourceCount = meaningfulEvidence
            .Where(item => item.Kind == EvidenceKind.DirectSnippet)
            .Select(item => item.SourceId)
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var progressIsPressured =
            context.DuplicateToolCallCount > 0 ||
            context.ConsecutiveNoProgressCount > 0 ||
            context.HadToolFailure ||
            context.NewEvidenceCount == 0;

        if (assessment.IsSufficient && assessment.ConfidenceScore >= 0.65)
        {
            return new ConvergenceDecision(
                ConvergenceDecisionType.FinalizeConfidentAnswer,
                "Evidence is sufficient with enough confidence to finalize.",
                "Use the assessed evidence directly and keep claims grounded to the cited sources.");
        }

        if (progressIsPressured && hasEvidence)
        {
            return new ConvergenceDecision(
                ConvergenceDecisionType.FinalizePartialAnswer,
                "Evidence is incomplete and recent execution did not add reliable progress.",
                "Answer with explicit limitations instead of continuing the same retrieval pattern.");
        }

        if (onlySearchHits)
        {
            return new ConvergenceDecision(
                ConvergenceDecisionType.ContinueWithDeeperRead,
                "Evidence is limited to candidate observations and needs direct file confirmation.",
                "Read the most relevant source file from the available evidence before making file-level claims.");
        }

        if (partialRatio > 0.5)
        {
            return new ConvergenceDecision(
                ConvergenceDecisionType.ContinueWithDeeperRead,
                "Most evidence is partial, truncated, or summary-level.",
                "Read or inspect a more complete direct source before finalizing.");
        }

        if (!hasDirectEvidence)
        {
            return new ConvergenceDecision(
                ConvergenceDecisionType.ContinueWithDeeperRead,
                "Evidence is limited to indirect observations and needs direct file confirmation.",
                "Read the most relevant source file from the available evidence before making file-level claims.");
        }

        if (directSourceCount < 2)
        {
            return new ConvergenceDecision(
                ConvergenceDecisionType.ContinueWithBroaderSearch,
                "Direct evidence exists, but it is concentrated in fewer than two source files.",
                "Search for related implementation files or supporting call sites to improve coverage.");
        }

        return new ConvergenceDecision(
            ConvergenceDecisionType.FinalizePartialAnswer,
            "Evidence is usable but does not meet the confidence and coverage threshold for a confident answer.",
            "Answer with caveats and identify what additional evidence would improve confidence.");
    }
}
