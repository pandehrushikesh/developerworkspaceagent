namespace ProjectLens.Application.Abstractions;

public interface IEvidenceEvaluator
{
    EvidenceAssessment Assess(IEnumerable<EvidenceItem> evidenceItems);
}
