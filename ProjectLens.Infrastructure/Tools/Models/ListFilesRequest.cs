namespace ProjectLens.Infrastructure.Tools.Models;

public sealed record ListFilesRequest(
    string Path,
    bool Recursive,
    int? MaxDepth);
