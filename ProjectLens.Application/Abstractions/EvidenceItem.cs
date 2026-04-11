namespace ProjectLens.Application.Abstractions;

public sealed record EvidenceItem(
    string ToolName,
    string SourceId,
    string Content,
    EvidenceKind Kind,
    bool IsPartial,
    double Confidence);
