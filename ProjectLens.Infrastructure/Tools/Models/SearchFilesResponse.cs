namespace ProjectLens.Infrastructure.Tools.Models;

public sealed record SearchFilesResponse(
    string SearchRoot,
    string Query,
    string FilePattern,
    bool CaseSensitive,
    int MaxResults,
    int TotalMatches,
    IReadOnlyCollection<SearchFileMatch> Matches);
