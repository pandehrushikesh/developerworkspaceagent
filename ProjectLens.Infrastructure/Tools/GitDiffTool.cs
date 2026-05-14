using ProjectLens.Domain;
using ProjectLens.Infrastructure.Tools.Models;

namespace ProjectLens.Infrastructure.Tools;

public sealed class GitDiffTool : ITool
{
    private const string DefaultFrom = "HEAD~1";
    private const string DefaultTo = "HEAD";
    private const int MaxHunkContentLength = 2000;

    private readonly GitCommandRunner _git;
    private readonly WorkspacePathResolver _pathResolver;

    public GitDiffTool(string workspaceRoot)
    {
        _git = new GitCommandRunner(workspaceRoot);
        _pathResolver = new WorkspacePathResolver(workspaceRoot);
    }

    public ToolDefinition Definition { get; } = new(
        "git_diff",
        "Shows the diff between two commits, branches, or tags. Returns changed files with addition/deletion counts and diff hunks.",
        new Dictionary<string, string>
        {
            ["from"] = $"Optional starting ref (commit hash, branch, tag). Defaults to '{DefaultFrom}'.",
            ["to"] = $"Optional ending ref (commit hash, branch, tag). Defaults to '{DefaultTo}'.",
            ["path"] = "Optional workspace-relative path to restrict the diff to a specific file or directory."
        });

    public async Task<ToolExecutionResult> ExecuteAsync(
        IReadOnlyDictionary<string, string> arguments,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_git.IsGitRepository())
            {
                return ToolResultFactory.Failure(Definition.Name, "The workspace is not a git repository.");
            }

            var request = ParseRequest(arguments);
            var args = BuildArgs(request);
            var result = await _git.RunAsync(args, cancellationToken);

            if (!result.Success)
            {
                return ToolResultFactory.Failure(Definition.Name, result.Error ?? "git diff failed.");
            }

            var files = ParseDiffOutput(result.Output);
            var response = new GitDiffResponse(request.From, request.To, files.Count, files);
            return ToolResultFactory.Success(Definition.Name, response);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return ToolResultFactory.Failure(Definition.Name, ex.Message);
        }
    }

    private GitDiffRequest ParseRequest(IReadOnlyDictionary<string, string> arguments)
    {
        var from = arguments.TryGetValue("from", out var rawFrom) && !string.IsNullOrWhiteSpace(rawFrom)
            ? rawFrom
            : DefaultFrom;

        var to = arguments.TryGetValue("to", out var rawTo) && !string.IsNullOrWhiteSpace(rawTo)
            ? rawTo
            : DefaultTo;

        var path = arguments.TryGetValue("path", out var rawPath) && !string.IsNullOrWhiteSpace(rawPath)
            ? rawPath
            : null;

        if (path is not null)
        {
            _pathResolver.ResolvePath(path);
        }

        return new GitDiffRequest(from, to, path);
    }

    private static List<string> BuildArgs(GitDiffRequest request)
    {
        var args = new List<string>
        {
            "diff",
            "--stat",       // file-level summary with +/-
            "-p",           // include patch (hunks)
            "--no-color",
            $"{request.From}..{request.To}"
        };

        if (request.Path is not null)
        {
            args.Add("--");
            args.Add(request.Path);
        }

        return args;
    }

    private static List<GitDiffFile> ParseDiffOutput(string output)
    {
        var files = new List<GitDiffFile>();
        if (string.IsNullOrWhiteSpace(output))
        {
            return files;
        }

        // Split on "diff --git" markers to get per-file blocks
        var blocks = output.Split("diff --git ", StringSplitOptions.RemoveEmptyEntries);

        foreach (var block in blocks)
        {
            var lines = block.Split('\n');
            if (lines.Length == 0)
            {
                continue;
            }

            // First line: "a/<path> b/<path>"
            var headerParts = lines[0].Split(' ');
            if (headerParts.Length < 2)
            {
                continue;
            }

            var filePath = headerParts[1].StartsWith("b/")
                ? headerParts[1][2..]
                : headerParts[1];

            var status = DetermineStatus(lines);
            var (additions, deletions) = CountChanges(lines);
            var hunks = ParseHunks(lines);

            files.Add(new GitDiffFile(filePath, status, additions, deletions, hunks));
        }

        return files;
    }

    private static string DetermineStatus(string[] lines)
    {
        foreach (var line in lines)
        {
            if (line.StartsWith("new file mode"))
            {
                return "added";
            }

            if (line.StartsWith("deleted file mode"))
            {
                return "deleted";
            }

            if (line.StartsWith("rename "))
            {
                return "renamed";
            }
        }

        return "modified";
    }

    private static (int Additions, int Deletions) CountChanges(string[] lines)
    {
        var additions = 0;
        var deletions = 0;

        foreach (var line in lines)
        {
            if (line.StartsWith('+') && !line.StartsWith("+++"))
            {
                additions++;
            }
            else if (line.StartsWith('-') && !line.StartsWith("---"))
            {
                deletions++;
            }
        }

        return (additions, deletions);
    }

    private static List<GitDiffHunk> ParseHunks(string[] lines)
    {
        var hunks = new List<GitDiffHunk>();
        string? currentHeader = null;
        var currentContent = new System.Text.StringBuilder();

        foreach (var line in lines)
        {
            if (line.StartsWith("@@"))
            {
                if (currentHeader is not null)
                {
                    hunks.Add(new GitDiffHunk(currentHeader, TrimHunkContent(currentContent.ToString())));
                }

                currentHeader = line;
                currentContent.Clear();
            }
            else if (currentHeader is not null)
            {
                currentContent.AppendLine(line);
            }
        }

        if (currentHeader is not null && currentContent.Length > 0)
        {
            hunks.Add(new GitDiffHunk(currentHeader, TrimHunkContent(currentContent.ToString())));
        }

        return hunks;
    }

    private static string TrimHunkContent(string content)
    {
        if (content.Length <= MaxHunkContentLength)
        {
            return content.TrimEnd();
        }

        return content[..MaxHunkContentLength].TrimEnd() + "\n... (truncated)";
    }
}
