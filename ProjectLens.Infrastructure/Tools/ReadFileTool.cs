using System.Text;
using ProjectLens.Domain;
using ProjectLens.Infrastructure.Tools.Models;

namespace ProjectLens.Infrastructure.Tools;

public sealed class ReadFileTool : ITool
{
    private readonly int _maxCharacters;
    private readonly WorkspacePathResolver _pathResolver;

    public ReadFileTool(string workspaceRoot, int maxCharacters = 8_000)
    {
        if (maxCharacters < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCharacters), "Max characters must be greater than 0.");
        }

        _maxCharacters = maxCharacters;
        _pathResolver = new WorkspacePathResolver(workspaceRoot);
    }

    public ToolDefinition Definition { get; } = new(
        "read_file",
        "Reads a text file from within the workspace.",
        new Dictionary<string, string>
        {
            ["path"] = "Workspace-relative or absolute file path within the workspace."
        });

    public async Task<ToolExecutionResult> ExecuteAsync(
        IReadOnlyDictionary<string, string> arguments,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = ParseRequest(arguments);
            var filePath = _pathResolver.ResolvePath(request.Path);

            if (!File.Exists(filePath))
            {
                return ToolResultFactory.Failure(Definition.Name, "The requested file does not exist.");
            }

            if (!TextFileDetector.IsTextFile(filePath))
            {
                return ToolResultFactory.Failure(Definition.Name, "Only text-based files can be read.");
            }

            var response = await ReadFileAsync(filePath, cancellationToken);
            return ToolResultFactory.Success(Definition.Name, response);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return ToolResultFactory.Failure(Definition.Name, exception.Message);
        }
    }

    private static ReadFileRequest ParseRequest(IReadOnlyDictionary<string, string> arguments)
    {
        if (!arguments.TryGetValue("path", out var path) || string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("The path argument is required.");
        }

        return new ReadFileRequest(path);
    }

    private async Task<ReadFileResponse> ReadFileAsync(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        using var reader = new StreamReader(stream, Encoding.UTF8, true);

        var buffer = new char[Math.Min(_maxCharacters, 2048)];
        var builder = new StringBuilder(Math.Min(_maxCharacters, 4096));
        var remainingCharacters = _maxCharacters;

        while (remainingCharacters > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var charsToRead = Math.Min(buffer.Length, remainingCharacters);
            var readCount = await reader.ReadAsync(buffer.AsMemory(0, charsToRead));

            if (readCount == 0)
            {
                break;
            }

            builder.Append(buffer, 0, readCount);
            remainingCharacters -= readCount;
        }

        var isTruncated = !reader.EndOfStream;
        var content = builder.ToString();

        return new ReadFileResponse(
            _pathResolver.ToRelativePath(filePath),
            content,
            isTruncated,
            content.Length);
    }
}
