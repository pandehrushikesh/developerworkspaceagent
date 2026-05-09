using ProjectLens.Domain;
using ProjectLens.Infrastructure.Tools.Models;

namespace ProjectLens.Infrastructure.Tools;

public sealed class EditFileTool : ITool
{
    private readonly WorkspacePathResolver _pathResolver;

    public EditFileTool(string workspaceRoot)
    {
        _pathResolver = new WorkspacePathResolver(workspaceRoot);
    }

    public ToolDefinition Definition { get; } = new(
        "edit_file",
        "Replaces an exact string in a file within the workspace. " +
        "Fails if the string is not found or appears more than once. " +
        "Use read_file first to see the current content before editing.",
        new Dictionary<string, string>
        {
            ["path"] = "Workspace-relative or absolute file path within the workspace.",
            ["old_string"] = "Exact text to find in the file. Must appear exactly once.",
            ["new_string"] = "Replacement text. Use an empty string to delete old_string."
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
                return ToolResultFactory.Failure(Definition.Name, "Only text-based files can be edited.");
            }

            var originalContent = await File.ReadAllTextAsync(filePath, cancellationToken);

            var occurrenceCount = CountOccurrences(originalContent, request.OldString);
            if (occurrenceCount == 0)
            {
                return ToolResultFactory.Failure(
                    Definition.Name,
                    "The old_string was not found in the file. Use read_file to verify the exact content.");
            }

            if (occurrenceCount > 1)
            {
                return ToolResultFactory.Failure(
                    Definition.Name,
                    $"The old_string appears {occurrenceCount} times in the file. " +
                    "Provide a longer, unique string to identify the exact location to edit.");
            }

            var updatedContent = originalContent.Replace(request.OldString, request.NewString, StringComparison.Ordinal);
            await File.WriteAllTextAsync(filePath, updatedContent, cancellationToken);

            var response = new EditFileResponse(_pathResolver.ToRelativePath(filePath), 1);
            return ToolResultFactory.Success(Definition.Name, response);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return ToolResultFactory.Failure(Definition.Name, exception.Message);
        }
    }

    private static EditFileRequest ParseRequest(IReadOnlyDictionary<string, string> arguments)
    {
        if (!arguments.TryGetValue("path", out var path) || string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("The path argument is required.");
        }

        if (!arguments.TryGetValue("old_string", out var oldString) || oldString is null)
        {
            throw new ArgumentException("The old_string argument is required.");
        }

        if (string.IsNullOrEmpty(oldString))
        {
            throw new ArgumentException("The old_string must not be empty. Use write_file to replace the entire file content.");
        }

        arguments.TryGetValue("new_string", out var newString);
        return new EditFileRequest(path, oldString, newString ?? string.Empty);
    }

    private static int CountOccurrences(string text, string pattern)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += pattern.Length;
        }

        return count;
    }
}
