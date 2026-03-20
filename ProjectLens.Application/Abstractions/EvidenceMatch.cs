namespace ProjectLens.Application.Abstractions;

public sealed record EvidenceMatch(
    string Path,
    string Snippet,
    int LineNumber = 0,
    string MatchKind = "keyword",
    double SimilarityScore = 0,
    int EndLineNumber = 0);
