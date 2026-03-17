namespace ProjectLens.Infrastructure.Tools.Models;

public sealed record SearchFileMatch(
    string Path,
    int LineNumber,
    string Snippet);
