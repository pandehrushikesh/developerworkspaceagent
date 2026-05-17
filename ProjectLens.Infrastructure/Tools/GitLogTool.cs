using ProjectLens.Domain;
using ProjectLens.Infrastructure.Tools.Models;

namespace ProjectLens.Infrastructure.Tools;

public sealed class GitLogTool : ITool
{
    private const int DefaultMaxCommits = 20;
    private const int AbsoluteMaxCommits = 100;
    private const string CommitSeparator = "---COMMIT---";
    private const string FieldSeparator = "\x1F";

    private readonly GitCommandRunner _git;
    private readonly WorkspacePathResolver _pathResolver;

    public GitLogTool(string workspaceRoot)
    {
        _git = new GitCommandRunner(workspaceRoot);
        _pathResolver = new WorkspacePathResolver(workspaceRoot);
    }

    public ToolDefinition Definition { get; } = new(
        "git_log",
        "Returns commit history for the workspace or a specific file/directory. Shows author, date, message, and files changed per commit.",
        new Dictionary<string, string>
        {
            ["path"] = "Optional workspace-relative path to filter commits to that file or directory.",
            ["maxCommits"] = $"Optional maximum number of commits to return (default {DefaultMaxCommits}, max {AbsoluteMaxCommits}).",
            ["since"] = "Optional date filter. Returns commits after this date. Accepts git date formats e.g. '2 weeks ago', '2024-01-01'.",
            ["author"] = "Optional author name or email filter (substring match)."
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
                return ToolResultFactory.Failure(Definition.Name, result.Error ?? "git log failed.");
            }

            var commits = ParseOutput(result.Output);
            var response = new GitLogResponse(request.Path, commits.Count, commits);
            return ToolResultFactory.Success(Definition.Name, response);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return ToolResultFactory.Failure(Definition.Name, ex.Message);
        }
    }

    private GitLogRequest ParseRequest(IReadOnlyDictionary<string, string> arguments)
    {
        var path = arguments.TryGetValue("path", out var rawPath) && !string.IsNullOrWhiteSpace(rawPath)
            ? rawPath
            : null;

        if (path is not null)
        {
            _pathResolver.ResolvePath(path);
        }

        var maxCommits = DefaultMaxCommits;
        if (arguments.TryGetValue("maxCommits", out var rawMax) && !string.IsNullOrWhiteSpace(rawMax))
        {
            if (!int.TryParse(rawMax, out maxCommits) || maxCommits < 1)
            {
                throw new ArgumentException("maxCommits must be a positive integer.");
            }

            maxCommits = Math.Min(maxCommits, AbsoluteMaxCommits);
        }

        var since = arguments.TryGetValue("since", out var rawSince) && !string.IsNullOrWhiteSpace(rawSince)
            ? rawSince
            : null;

        var author = arguments.TryGetValue("author", out var rawAuthor) && !string.IsNullOrWhiteSpace(rawAuthor)
            ? rawAuthor
            : null;

        return new GitLogRequest(path, maxCommits, since, author);
    }

    private static List<string> BuildArgs(GitLogRequest request)
    {
        var args = new List<string>
        {
            "log",
            $"--format={CommitSeparator}%n%H{FieldSeparator}%an{FieldSeparator}%ae{FieldSeparator}%ai{FieldSeparator}%s",
            "--name-only",
            $"-n{request.MaxCommits}"
        };

        if (request.Since is not null)
        {
            args.Add($"--since={request.Since}");
        }

        if (request.Author is not null)
        {
            args.Add($"--author={request.Author}");
        }

        if (request.Path is not null)
        {
            args.Add("--");
            args.Add(request.Path);
        }

        return args;
    }

    private static List<GitCommit> ParseOutput(string output)
    {
        var commits = new List<GitCommit>();
        var blocks = output.Split(CommitSeparator, StringSplitOptions.RemoveEmptyEntries);

        foreach (var block in blocks)
        {
            var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Length == 0)
            {
                continue;
            }

            var headerLine = lines[0];
            var parts = headerLine.Split(FieldSeparator);
            if (parts.Length < 5)
            {
                continue;
            }

            var hash = parts[0].Trim();
            var author = parts[1].Trim();
            var email = parts[2].Trim();
            var date = parts[3].Trim();
            var message = parts[4].Trim();

            var filesChanged = lines.Skip(1)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();

            commits.Add(new GitCommit(
                hash,
                hash.Length >= 7 ? hash[..7] : hash,
                author,
                email,
                date,
                message,
                filesChanged));
        }

        return commits;
    }
}
