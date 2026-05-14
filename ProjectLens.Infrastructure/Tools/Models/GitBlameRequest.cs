namespace ProjectLens.Infrastructure.Tools.Models;

public sealed record GitBlameRequest(
    string Path,
    int? StartLine,
    int? EndLine);
