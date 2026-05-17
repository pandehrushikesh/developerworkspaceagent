namespace ProjectLens.Domain;

public sealed record AgentSessionState
{
    public required string SessionId { get; init; }

    public required string WorkspacePath { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }

    public string WorkingSummary { get; init; } = string.Empty;

    public IReadOnlyCollection<string> VisitedFiles { get; init; } = Array.Empty<string>();

    public IReadOnlyCollection<string> RecentToolHistory { get; init; } = Array.Empty<string>();

    /// <summary>
    /// The agent's most recent response text.  Injected into the next turn's
    /// instructions so the model knows what question or answer it just gave,
    /// which enables it to correctly interpret short follow-up replies like "yes".
    /// </summary>
    public string LastAgentResponse { get; init; } = string.Empty;
}
