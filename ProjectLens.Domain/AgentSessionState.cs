namespace ProjectLens.Domain;

public sealed record AgentSessionState
{
    public required string SessionId { get; init; }

    public required string WorkspacePath { get; init; }

    public string WorkingSummary { get; init; } = string.Empty;

    public IReadOnlyCollection<string> VisitedFiles { get; init; } = Array.Empty<string>();

    public IReadOnlyCollection<string> RecentToolHistory { get; init; } = Array.Empty<string>();
}
