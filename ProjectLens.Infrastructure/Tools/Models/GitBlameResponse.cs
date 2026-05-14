namespace ProjectLens.Infrastructure.Tools.Models;

public sealed record GitBlameResponse(
    string Path,
    IReadOnlyList<GitBlameLine> Lines);

public sealed record GitBlameLine(
    int LineNumber,
    string Content,
    string CommitHash,
    string Author,
    string Date,
    string Summary);
