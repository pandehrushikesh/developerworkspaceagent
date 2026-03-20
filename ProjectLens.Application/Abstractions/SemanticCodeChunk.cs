namespace ProjectLens.Application.Abstractions;

public sealed record SemanticCodeChunk(
    string Path,
    string Text,
    int StartLine,
    int EndLine,
    string? ClassName = null,
    string? MethodName = null);
