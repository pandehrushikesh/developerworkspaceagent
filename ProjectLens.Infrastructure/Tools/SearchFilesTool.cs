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
    private readonly ISemanticSearchService? _semanticSearchService;

    public SearchFilesTool(
        string workspaceRoot,
        IEvidenceQualityEvaluator? evidenceQualityEvaluator = null,
        ISemanticSearchService? semanticSearchService = null)
    {
        _evidenceQualityEvaluator = evidenceQualityEvaluator ?? new RuleBasedEvidenceQualityEvaluator();
        _pathResolver = new WorkspacePathResolver(workspaceRoot);
        _semanticSearchService = semanticSearchService;
    }

    public ToolDefinition Definition { get; } = new(
        "search_files",
        "Searches text-based files in the workspace using exact matches first and bounded semantic chunk retrieval when helpful, then returns grounded candidate files with snippets.",
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
            var context = BuildExecutionContext(arguments);
            var keywordMatches = await CollectKeywordMatchesAsync(context, cancellationToken);
            var keywordAssessment = AssessKeywordEvidence(context, keywordMatches);
            var semanticMatches = await CollectSemanticMatchesAsync(context, keywordAssessment, cancellationToken);
            var rankedMatches = RankCombinedMatches(context, keywordMatches, semanticMatches);
            return ToolResultFactory.Success(Definition.Name, BuildResponse(context, keywordMatches, semanticMatches, rankedMatches));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return ToolResultFactory.Failure(Definition.Name, exception.Message);
        }
    }

    private SearchExecutionContext BuildExecutionContext(IReadOnlyDictionary<string, string> arguments)
    {
        var request = ParseRequest(arguments);
        var targetPath = _pathResolver.ResolvePath(request.Path);
        if (!File.Exists(targetPath) && !Directory.Exists(targetPath))
        {
            throw new InvalidOperationException("The requested path does not exist.");
        }

        return new SearchExecutionContext(
            request,
            targetPath,
            request.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase,
            Math.Max(request.MaxResults * 5, 50));
    }

    private async Task<SearchFileMatch[]> CollectKeywordMatchesAsync(
        SearchExecutionContext context,
        CancellationToken cancellationToken)
    {
        var matches = new List<SearchFileMatch>(context.Request.MaxResults * 2);
        foreach (var filePath in EnumerateCandidateFiles(context.TargetPath, context.Request.FilePattern))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!TextFileDetector.IsTextFile(filePath))
            {
                continue;
            }

            await CollectMatchesAsync(
                filePath,
                context.Request,
                context.Comparison,
                matches,
                context.MaxCandidateResults,
                cancellationToken);
        }

        return matches.ToArray();
    }

    private SearchEvidenceAssessment AssessKeywordEvidence(
        SearchExecutionContext context,
        IReadOnlyCollection<SearchFileMatch> keywordMatches)
    {
        var keywordEvidence = keywordMatches
            .Select(match => new EvidenceMatch(match.Path, match.Snippet, match.LineNumber))
            .ToArray();

        return _evidenceQualityEvaluator.AssessSearchEvidence(
            keywordEvidence,
            context.Request.Query,
            context.Request.MaxResults);
    }

    private async Task<SearchFileMatch[]> CollectSemanticMatchesAsync(
        SearchExecutionContext context,
        SearchEvidenceAssessment keywordAssessment,
        CancellationToken cancellationToken)
    {
        if (_semanticSearchService is null || !ShouldUseSemanticSearch(context.Request.Query, keywordAssessment))
        {
            return Array.Empty<SearchFileMatch>();
        }

        var semanticResults = await _semanticSearchService.SearchAsync(
            context.Request.Query,
            context.TargetPath,
            context.Request.FilePattern,
            Math.Min(5, context.Request.MaxResults),
            cancellationToken);

        return semanticResults
            .Select(match => new SearchFileMatch(
                match.Path,
                match.StartLine,
                BuildSnippet(match.ChunkText),
                "semantic",
                match.SimilarityScore,
                match.EndLine,
                match.ClassName,
                match.MethodName))
            .ToArray();
    }

    private SearchFileMatch[] RankCombinedMatches(
        SearchExecutionContext context,
        IReadOnlyCollection<SearchFileMatch> keywordMatches,
        IReadOnlyCollection<SearchFileMatch> semanticMatches)
    {
        var combinedEvidence = ToEvidenceMatches(keywordMatches)
            .Concat(ToEvidenceMatches(semanticMatches))
            .GroupBy(match => $"{match.Path}|{match.LineNumber}|{match.MatchKind}|{match.Snippet}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        return _evidenceQualityEvaluator
            .RankMatches(combinedEvidence, context.Request.Query, context.Request.MaxResults)
            .Select(match => RehydrateMatch(match, semanticMatches))
            .ToArray();
    }

    private SearchFilesResponse BuildResponse(
        SearchExecutionContext context,
        IReadOnlyCollection<SearchFileMatch> keywordMatches,
        IReadOnlyCollection<SearchFileMatch> semanticMatches,
        IReadOnlyCollection<SearchFileMatch> rankedMatches)
    {
        return new SearchFilesResponse(
            _pathResolver.ToRelativePath(context.TargetPath),
            context.Request.Query,
            context.Request.FilePattern,
            context.Request.CaseSensitive,
            context.Request.MaxResults,
            rankedMatches.Count,
            rankedMatches,
            DetermineRetrievalMode(keywordMatches.Count, semanticMatches.Count),
            keywordMatches.Count,
            semanticMatches.Count);
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

    private bool ShouldUseSemanticSearch(
        string query,
        SearchEvidenceAssessment keywordAssessment)
    {
        return keywordAssessment.IsWeakEvidence ||
            _evidenceQualityEvaluator.IsConceptualQuery(query);
    }

    private static IEnumerable<EvidenceMatch> ToEvidenceMatches(IEnumerable<SearchFileMatch> matches)
    {
        return matches.Select(match => new EvidenceMatch(
            match.Path,
            match.Snippet,
            match.LineNumber,
            match.MatchKind,
            match.SimilarityScore,
            match.EndLineNumber));
    }

    private static SearchFileMatch RehydrateMatch(
        EvidenceMatch match,
        IEnumerable<SearchFileMatch> semanticMatches)
    {
        return semanticMatches.FirstOrDefault(candidate =>
                   string.Equals(candidate.Path, match.Path, StringComparison.OrdinalIgnoreCase) &&
                   candidate.LineNumber == match.LineNumber &&
                   candidate.MatchKind.Equals(match.MatchKind, StringComparison.OrdinalIgnoreCase))
               ?? new SearchFileMatch(
                   match.Path,
                   match.LineNumber,
                   match.Snippet,
                   match.MatchKind,
                   match.SimilarityScore,
                   match.EndLineNumber);
    }

    private static string DetermineRetrievalMode(int keywordMatchCount, int semanticMatchCount)
    {
        return semanticMatchCount > 0 && keywordMatchCount > 0
            ? "hybrid"
            : semanticMatchCount > 0
                ? "semantic"
                : "keyword";
    }

    private sealed record SearchExecutionContext(
        SearchFilesRequest Request,
        string TargetPath,
        StringComparison Comparison,
        int MaxCandidateResults);
}
