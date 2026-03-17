namespace ProjectLens.Infrastructure.Tools.Models;

public sealed record ReadFileResponse(
    string Path,
    string Content,
    bool IsTruncated,
    int CharacterCount);
