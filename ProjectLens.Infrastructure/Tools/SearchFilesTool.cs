using System.IO.Enumeration;
using System.Text;
using ProjectLens.Application.Abstractions;
using ProjectLens.Domain;
using ProjectLens.Infrastructure.Tools.Models;

namespace ProjectLens.Infrastructure.Tools;

public sealed class SearchFilesTool : ITool
{
    private const int MaxSnippetLength = 160;

    private readonly IEvidenceQualityEvaluator _evidenceQualityEvaluator;
    private readonly WorkspacePathResolver _pathResolver;

    public SearchFilesTool(
        string workspaceRoot,
        IEvidenceQualityEvaluator? evidenceQualityEvaluator = null)
    {
        _evidenceQualityEvaluator = evidenceQualityEvaluator ?? new RuleBasedEvidenceQualityEvaluator();
        _pathResolver = new WorkspacePathResolver(workspaceRoot);
    }

    public ToolDefinition Definition { get; } = new(
        "search_files",
        "Searches text-based files in the workspace for a query string and returns matching files with snippets.",
        new Dictionary<string, string>
        {
            ["query"] = "Required search text.",
            ["path"] = "Optional workspace-relative or absolute path within the workspace.",
            ["filePattern"] = "Optional filename glob or pattern filter.",
            ["maxResults"] = "Optional maximum number of matches to return.",
            ["caseSensitive"] = "Optional flag that controls case-sensitive matching."
        });

    public async Task<ToolExecutionResult> ExecuteAsync(
        IReadOnlyDictionary<string, string> arguments,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = ParseRequest(arguments);
            var targetPath = _pathResolver.ResolvePath(request.Path);
            var maxCandidateResults = Math.Max(request.MaxResults * 5, 50);

            if (!File.Exists(targetPath) && !Directory.Exists(targetPath))
            {
                return ToolResultFactory.Failure(Definition.Name, "The requested path does not exist.");
            }

            var matches = new List<SearchFileMatch>(request.MaxResults * 2);
            var comparison = request.CaseSensitive
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;

            foreach (var filePath in EnumerateCandidateFiles(targetPath, request.FilePattern))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!TextFileDetector.IsTextFile(filePath))
                {
                    continue;
                }

                await CollectMatchesAsync(filePath, request, comparison, matches, maxCandidateResults, cancellationToken);

            }

            var rankedMatches = _evidenceQualityEvaluator
                .RankMatches(
                    matches.Select(match => new EvidenceMatch(match.Path, match.Snippet, match.LineNumber)),
                    request.Query,
                    request.MaxResults)
                .Select(match => new SearchFileMatch(match.Path, match.LineNumber, match.Snippet))
                .ToArray();

            var response = new SearchFilesResponse(
                _pathResolver.ToRelativePath(targetPath),
                request.Query,
                request.FilePattern,
                request.CaseSensitive,
                request.MaxResults,
                rankedMatches.Length,
                rankedMatches);

            return ToolResultFactory.Success(Definition.Name, response);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return ToolResultFactory.Failure(Definition.Name, exception.Message);
        }
    }

    private static SearchFilesRequest ParseRequest(IReadOnlyDictionary<string, string> arguments)
    {
        if (!arguments.TryGetValue("query", out var query) || string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("The query argument is required.");
        }

        var path = arguments.TryGetValue("path", out var rawPath) && !string.IsNullOrWhiteSpace(rawPath)
            ? rawPath
            : ".";

        var filePattern = arguments.TryGetValue("filePattern", out var rawPattern) && !string.IsNullOrWhiteSpace(rawPattern)
            ? rawPattern
            : "*";

        var maxResults = 20;
        if (arguments.TryGetValue("maxResults", out var rawMaxResults) && !string.IsNullOrWhiteSpace(rawMaxResults))
        {
            if (!int.TryParse(rawMaxResults, out maxResults) || maxResults < 1)
            {
                throw new ArgumentException("The maxResults argument must be an integer greater than 0.");
            }
        }

        var caseSensitive = false;
        if (arguments.TryGetValue("caseSensitive", out var rawCaseSensitive)
            && !string.IsNullOrWhiteSpace(rawCaseSensitive)
            && !bool.TryParse(rawCaseSensitive, out caseSensitive))
        {
            throw new ArgumentException("The caseSensitive argument must be true or false.");
        }

        return new SearchFilesRequest(query, path, filePattern, maxResults, caseSensitive);
    }

    private IEnumerable<string> EnumerateCandidateFiles(string targetPath, string filePattern)
    {
        if (File.Exists(targetPath))
        {
            if (MatchesPattern(targetPath, filePattern))
            {
                yield return targetPath;
            }

            yield break;
        }

        foreach (var filePath in Directory
            .EnumerateFiles(targetPath, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            if (MatchesPattern(filePath, filePattern))
            {
                yield return filePath;
            }
        }
    }

    private async Task CollectMatchesAsync(
        string filePath,
        SearchFilesRequest request,
        StringComparison comparison,
        List<SearchFileMatch> matches,
        int maxCandidateResults,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        using var reader = new StreamReader(stream, Encoding.UTF8, true);

        var lineNumber = 0;
        while (!reader.EndOfStream && matches.Count < maxCandidateResults)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(cancellationToken) ?? string.Empty;
            lineNumber++;

            if (line.IndexOf(request.Query, comparison) < 0)
            {
                continue;
            }

            matches.Add(new SearchFileMatch(
                _pathResolver.ToRelativePath(filePath),
                lineNumber,
                BuildSnippet(line)));
        }
    }

    private static string BuildSnippet(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length <= MaxSnippetLength)
        {
            return trimmed;
        }

        return trimmed[..(MaxSnippetLength - 3)] + "...";
    }

    private static bool MatchesPattern(string filePath, string filePattern)
    {
        return FileSystemName.MatchesSimpleExpression(filePattern, Path.GetFileName(filePath), ignoreCase: true);
    }
}
