namespace ProjectLens.Infrastructure.Tools.Models;

public sealed record ListFilesResponse(
    string RootPath,
    IReadOnlyCollection<WorkspaceEntry> Entries);
