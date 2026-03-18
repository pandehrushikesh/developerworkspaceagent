namespace ProjectLens.Application.Abstractions;

public interface IFileCompressor
{
    string Compress(
        string filePath,
        string content,
        string? focusHint = null);
}
