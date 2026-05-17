namespace ProjectLens.Domain;

public sealed record AgentResponse(
    string Output,
    IReadOnlyCollection<AgentExecutionStep>? ExecutionSteps = null,
    IReadOnlyCollection<ToolExecutionResult>? ToolResults = null,
    bool Success = true,
    string? ErrorMessage = null,
    IReadOnlyCollection<AgentEvidenceItem>? EvidenceItems = null,
    AgentEvidenceAssessment? FinalAssessment = null);

public sealed record AgentEvidenceItem(
    string ToolName,
    string SourceId,
    string Content,
    string Kind,
    bool IsPartial,
    double Confidence);

public sealed record AgentEvidenceAssessment(
    bool IsSufficient,
    double CoverageScore,
    double ConfidenceScore,
    string Reason,
    IReadOnlyCollection<string> MissingAreas);
