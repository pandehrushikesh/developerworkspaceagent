namespace ProjectLens.Infrastructure.Tools.Models;

public sealed record SearchFilesRequest(
    string Query,
    string Path,
    string FilePattern,
    int MaxResults,
    bool CaseSensitive);
