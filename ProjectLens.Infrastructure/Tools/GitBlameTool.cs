using ProjectLens.Domain;
using ProjectLens.Infrastructure.Tools.Models;

namespace ProjectLens.Infrastructure.Tools;

public sealed class GitBlameTool : ITool
{
    private readonly GitCommandRunner _git;
    private readonly WorkspacePathResolver _pathResolver;

    public GitBlameTool(string workspaceRoot)
    {
        _git = new GitCommandRunner(workspaceRoot);
        _pathResolver = new WorkspacePathResolver(workspaceRoot);
    }

    public ToolDefinition Definition { get; } = new(
        "git_blame",
        "Shows which commit and author last modified each line of a file. Use to trace the origin of specific code.",
        new Dictionary<string, string>
        {
            ["path"] = "Required workspace-relative path to the file to blame.",
            ["startLine"] = "Optional first line number of the range to blame.",
            ["endLine"] = "Optional last line number of the range to blame."
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
            var resolvedPath = _pathResolver.ResolvePath(request.Path);

            if (!File.Exists(resolvedPath))
            {
                return ToolResultFactory.Failure(Definition.Name, "The requested file does not exist.");
            }

            var args = BuildArgs(request);
            var result = await _git.RunAsync(args, cancellationToken);

            if (!result.Success)
            {
                return ToolResultFactory.Failure(Definition.Name, result.Error ?? "git blame failed.");
            }

            var lines = ParsePorcelainOutput(result.Output);
            var response = new GitBlameResponse(_pathResolver.ToRelativePath(resolvedPath), lines);
            return ToolResultFactory.Success(Definition.Name, response);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return ToolResultFactory.Failure(Definition.Name, ex.Message);
        }
    }

    private GitBlameRequest ParseRequest(IReadOnlyDictionary<string, string> arguments)
    {
        if (!arguments.TryGetValue("path", out var path) || string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("The path argument is required.");
        }

        int? startLine = null;
        if (arguments.TryGetValue("startLine", out var rawStart) && !string.IsNullOrWhiteSpace(rawStart))
        {
            if (!int.TryParse(rawStart, out var s) || s < 1)
            {
                throw new ArgumentException("startLine must be a positive integer.");
            }

            startLine = s;
        }

        int? endLine = null;
        if (arguments.TryGetValue("endLine", out var rawEnd) && !string.IsNullOrWhiteSpace(rawEnd))
        {
            if (!int.TryParse(rawEnd, out var e) || e < 1)
            {
                throw new ArgumentException("endLine must be a positive integer.");
            }

            endLine = e;
        }

        if (startLine.HasValue && endLine.HasValue && endLine < startLine)
        {
            throw new ArgumentException("endLine must be greater than or equal to startLine.");
        }

        return new GitBlameRequest(path, startLine, endLine);
    }

    private List<string> BuildArgs(GitBlameRequest request)
    {
        var args = new List<string> { "blame", "--porcelain" };

        if (request.StartLine.HasValue && request.EndLine.HasValue)
        {
            args.Add($"-L{request.StartLine},{request.EndLine}");
        }
        else if (request.StartLine.HasValue)
        {
            args.Add($"-L{request.StartLine},+50");
        }

        args.Add("--");
        args.Add(request.Path);
        return args;
    }

    // Parses `git blame --porcelain` output format
    private static List<GitBlameLine> ParsePorcelainOutput(string output)
    {
        var lines = new List<GitBlameLine>();
        var commitInfo = new Dictionary<string, (string Author, string Date, string Summary)>(StringComparer.Ordinal);

        var rawLines = output.Split('\n');
        var i = 0;

        while (i < rawLines.Length)
        {
            var line = rawLines[i];

            // Header line: <hash> <orig-line> <final-line> [<num-lines>]
            if (line.Length >= 40 && !line.StartsWith('\t'))
            {
                var parts = line.Split(' ');
                if (parts.Length < 3)
                {
                    i++;
                    continue;
                }

                var hash = parts[0];
                if (!int.TryParse(parts[2], out var finalLine))
                {
                    i++;
                    continue;
                }

                string author = string.Empty;
                string date = string.Empty;
                string summary = string.Empty;

                // Read commit metadata lines
                i++;
                while (i < rawLines.Length && !rawLines[i].StartsWith('\t'))
                {
                    var meta = rawLines[i];
                    if (meta.StartsWith("author "))
                    {
                        author = meta["author ".Length..].Trim();
                    }
                    else if (meta.StartsWith("author-time "))
                    {
                        if (long.TryParse(meta["author-time ".Length..].Trim(), out var epoch))
                        {
                            date = DateTimeOffset.FromUnixTimeSeconds(epoch).ToString("yyyy-MM-ddTHH:mm:sszzz");
                        }
                    }
                    else if (meta.StartsWith("summary "))
                    {
                        summary = meta["summary ".Length..].Trim();
                    }

                    i++;
                }

                commitInfo[hash] = (author, date, summary);

                // Tab-prefixed line is the actual code content
                if (i < rawLines.Length && rawLines[i].StartsWith('\t'))
                {
                    var content = rawLines[i][1..]; // strip the leading tab
                    lines.Add(new GitBlameLine(finalLine, content, hash, author, date, summary));
                    i++;
                }
            }
            else
            {
                i++;
            }
        }

        return lines;
    }
}
