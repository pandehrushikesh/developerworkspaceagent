namespace ProjectLens.Api.Models;

public sealed record EvidenceItemDto(
    string ToolName,
    string SourceId,
    string Content,
    string Kind,
    bool IsPartial,
    double Confidence);
