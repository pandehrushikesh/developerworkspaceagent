namespace ProjectLens.Infrastructure.Tools.Models;

public sealed record GitLogResponse(
    string? FilterPath,
    int TotalReturned,
    IReadOnlyList<GitCommit> Commits);

public sealed record GitCommit(
    string Hash,
    string ShortHash,
    string Author,
    string Email,
    string Date,
    string Message,
    IReadOnlyList<string> FilesChanged);
