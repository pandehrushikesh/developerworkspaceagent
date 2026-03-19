namespace ProjectLens.Application.Abstractions;

public sealed record EvidenceMatch(
    string Path,
    string Snippet,
    int LineNumber = 0);
