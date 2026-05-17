namespace ProjectLens.Infrastructure.Tools.Models;

public sealed record GitLogRequest(
    string? Path,
    int MaxCommits,
    string? Since,
    string? Author);
