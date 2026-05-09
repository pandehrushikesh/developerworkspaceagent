namespace ProjectLens.Infrastructure.Tools.Models;

internal sealed record EditFileRequest(string Path, string OldString, string NewString);
