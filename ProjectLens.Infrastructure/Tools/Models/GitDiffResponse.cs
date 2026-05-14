namespace ProjectLens.Infrastructure.Tools.Models;

public sealed record GitDiffResponse(
    string From,
    string To,
    int TotalFilesChanged,
    IReadOnlyList<GitDiffFile> Files);

public sealed record GitDiffFile(
    string Path,
    string Status,
    int Additions,
    int Deletions,
    IReadOnlyList<GitDiffHunk> Hunks);

public sealed record GitDiffHunk(
    string Header,
    string Content);
