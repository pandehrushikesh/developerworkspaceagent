namespace ProjectLens.Infrastructure.Tools;

internal sealed class WorkspacePathResolver
{
    public WorkspacePathResolver(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            throw new ArgumentException("Workspace root is required.", nameof(workspaceRoot));
        }

        WorkspaceRoot = Normalize(workspaceRoot);
    }

    public string WorkspaceRoot { get; }

    public string ResolvePath(string requestedPath)
    {
        var candidate = string.IsNullOrWhiteSpace(requestedPath)
            ? WorkspaceRoot
            : requestedPath;

        if (!Path.IsPathRooted(candidate))
        {
            candidate = Path.Combine(WorkspaceRoot, candidate);
        }

        var fullPath = Normalize(candidate);

        if (!IsWithinWorkspace(fullPath))
        {
            throw new InvalidOperationException("The requested path is outside the workspace root.");
        }

        return fullPath;
    }

    public string ToRelativePath(string fullPath)
    {
        var relativePath = Path.GetRelativePath(WorkspaceRoot, Normalize(fullPath));
        return relativePath == "."
            ? "."
            : relativePath.Replace('\\', '/');
    }

    private bool IsWithinWorkspace(string fullPath)
    {
        return fullPath.Equals(WorkspaceRoot, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(WorkspaceRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string path)
    {
        return Path.GetFullPath(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }
}
