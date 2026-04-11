namespace ProjectLens.Application.Abstractions;

public sealed record RecoveryState(
    bool HasPendingWeakSearchEvidence,
    string? WeakSearchRecoveryGuidance)
{
    public static RecoveryState Empty { get; } = new(false, null);
}
