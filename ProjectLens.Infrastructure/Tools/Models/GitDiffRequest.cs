namespace ProjectLens.Infrastructure.Tools.Models;

public sealed record GitDiffRequest(
    string From,
    string To,
    string? Path);
