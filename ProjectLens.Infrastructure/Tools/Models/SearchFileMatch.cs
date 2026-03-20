namespace ProjectLens.Infrastructure.Tools.Models;

public sealed record SearchFileMatch(
    string Path,
    int LineNumber,
    string Snippet,
    string MatchKind = "keyword",
    double SimilarityScore = 0,
    int EndLineNumber = 0,
    string? ClassName = null,
    string? MethodName = null);
