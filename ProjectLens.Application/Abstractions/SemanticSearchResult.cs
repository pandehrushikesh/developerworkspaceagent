namespace ProjectLens.Application.Abstractions;

public sealed record SemanticSearchResult(
    string Path,
    string ChunkText,
    double SimilarityScore,
    int StartLine,
    int EndLine,
    string? ClassName = null,
    string? MethodName = null);
