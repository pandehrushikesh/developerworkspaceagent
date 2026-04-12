namespace ProjectLens.Application.Abstractions;

public interface IConvergencePolicy
{
    ConvergenceDecision Evaluate(
        EvidenceAssessment assessment,
        IReadOnlyCollection<EvidenceItem> evidenceItems,
        ConvergenceContext context);
}
