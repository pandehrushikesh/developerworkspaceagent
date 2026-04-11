namespace ProjectLens.Application.Abstractions;

public sealed record ToolOutputAdapterResult(
    ToolAdaptationResult Adaptation,
    AggregatedEvidenceContext? AggregatedEvidenceContext)
{
    public ToolOutputAdapterResult(
        string output,
        AggregatedEvidenceContext? aggregatedEvidenceContext)
        : this(
            new ToolAdaptationResult(output, Array.Empty<EvidenceItem>()),
            aggregatedEvidenceContext)
    {
    }

    public string Output => Adaptation.PromptText;

    public IReadOnlyCollection<EvidenceItem> EvidenceItems => Adaptation.EvidenceItems;
}
