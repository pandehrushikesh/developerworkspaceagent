using System.Text;
using ProjectLens.Domain;
using ProjectLens.Infrastructure.Tools.Models;

namespace ProjectLens.Infrastructure.Tools;

public sealed class WriteFileTool : ITool
{
    private readonly WorkspacePathResolver _pathResolver;

    public WriteFileTool(string workspaceRoot)
    {
        _pathResolver = new WorkspacePathResolver(workspaceRoot);
    }

    public ToolDefinition Definition { get; } = new(
        "write_file",
        "Creates or overwrites a text file within the workspace. Parent directories are created automatically.",
        new Dictionary<string, string>
        {
            ["path"] = "Workspace-relative or absolute file path within the workspace.",
            ["content"] = "Full text content to write to the file."
        });

    public async Task<ToolExecutionResult> ExecuteAsync(
        IReadOnlyDictionary<string, string> arguments,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = ParseRequest(arguments);
            var filePath = _pathResolver.ResolvePath(request.Path);

            var created = !File.Exists(filePath);

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var bytes = Encoding.UTF8.GetBytes(request.Content);
            await File.WriteAllBytesAsync(filePath, bytes, cancellationToken);

            var response = new WriteFileResponse(
                _pathResolver.ToRelativePath(filePath),
                bytes.Length,
                created);

            return ToolResultFactory.Success(Definition.Name, response);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return ToolResultFactory.Failure(Definition.Name, exception.Message);
        }
    }

    private static WriteFileRequest ParseRequest(IReadOnlyDictionary<string, string> arguments)
    {
        if (!arguments.TryGetValue("path", out var path) || string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("The path argument is required.");
        }

        arguments.TryGetValue("content", out var content);
        return new WriteFileRequest(path, content ?? string.Empty);
    }
}
