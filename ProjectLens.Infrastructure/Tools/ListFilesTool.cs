using ProjectLens.Domain;
using ProjectLens.Infrastructure.Tools.Models;

namespace ProjectLens.Infrastructure.Tools;

public sealed class ListFilesTool : ITool
{
    private readonly WorkspacePathResolver _pathResolver;

    public ListFilesTool(string workspaceRoot)
    {
        _pathResolver = new WorkspacePathResolver(workspaceRoot);
    }

    public ToolDefinition Definition { get; } = new(
        "list_files",
        "Lists files and folders within the workspace.",
        new Dictionary<string, string>
        {
            ["path"] = "Workspace-relative or absolute path within the workspace.",
            ["recursive"] = "When true, lists nested entries.",
            ["maxDepth"] = "Optional maximum recursive depth. Children of the target path are depth 1."
        });

    public Task<ToolExecutionResult> ExecuteAsync(
        IReadOnlyDictionary<string, string> arguments,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = ParseRequest(arguments);
            var targetPath = _pathResolver.ResolvePath(request.Path);

            if (!Directory.Exists(targetPath))
            {
                return Task.FromResult(
                    ToolResultFactory.Failure(Definition.Name, "The requested path does not exist or is not a directory."));
            }

            var entries = EnumerateEntries(targetPath, request, cancellationToken);
            var response = new ListFilesResponse(_pathResolver.ToRelativePath(targetPath), entries);

            return Task.FromResult(ToolResultFactory.Success(Definition.Name, response));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Task.FromResult(ToolResultFactory.Failure(Definition.Name, exception.Message));
        }
    }

    private static ListFilesRequest ParseRequest(IReadOnlyDictionary<string, string> arguments)
    {
        var path = arguments.TryGetValue("path", out var rawPath) && !string.IsNullOrWhiteSpace(rawPath)
            ? rawPath
            : ".";

        var recursive = false;
        if (arguments.TryGetValue("recursive", out var rawRecursive)
            && !bool.TryParse(rawRecursive, out recursive))
        {
            throw new ArgumentException("The recursive argument must be true or false.");
        }

        int? maxDepth = null;
        if (arguments.TryGetValue("maxDepth", out var rawMaxDepth)
            && !string.IsNullOrWhiteSpace(rawMaxDepth))
        {
            if (!int.TryParse(rawMaxDepth, out var parsedMaxDepth) || parsedMaxDepth < 1)
            {
                throw new ArgumentException("The maxDepth argument must be an integer greater than 0.");
            }

            maxDepth = parsedMaxDepth;
        }

        return new ListFilesRequest(path, recursive, maxDepth);
    }

    private IReadOnlyList<WorkspaceEntry> EnumerateEntries(
        string rootPath,
        ListFilesRequest request,
        CancellationToken cancellationToken)
    {
        var entries = new List<WorkspaceEntry>();
        var directoriesToVisit = new Stack<(string Path, int Depth)>();
        directoriesToVisit.Push((rootPath, 0));

        while (directoriesToVisit.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (currentPath, depth) = directoriesToVisit.Pop();

            foreach (var directoryPath in Directory.EnumerateDirectories(currentPath).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                entries.Add(new WorkspaceEntry(_pathResolver.ToRelativePath(directoryPath), true));

                if (request.Recursive)
                {
                    var nextDepth = depth + 1;
                    if (!request.MaxDepth.HasValue || nextDepth < request.MaxDepth.Value)
                    {
                        directoriesToVisit.Push((directoryPath, nextDepth));
                    }
                }
            }

            foreach (var filePath in Directory.EnumerateFiles(currentPath).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                entries.Add(new WorkspaceEntry(_pathResolver.ToRelativePath(filePath), false));
            }

            if (!request.Recursive)
            {
                break;
            }
        }

        return entries
            .OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.IsDirectory ? 0 : 1)
            .ToArray();
    }
}
