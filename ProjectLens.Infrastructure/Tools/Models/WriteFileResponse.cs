namespace ProjectLens.Infrastructure.Tools.Models;

internal sealed record WriteFileResponse(string Path, int BytesWritten, bool Created);
