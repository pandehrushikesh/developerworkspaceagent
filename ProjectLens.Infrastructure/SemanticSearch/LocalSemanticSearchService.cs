using System.Collections.Concurrent;
using System.IO.Enumeration;
using ProjectLens.Application.Abstractions;
using ProjectLens.Infrastructure.Tools;

namespace ProjectLens.Infrastructure.SemanticSearch;

public sealed class LocalSemanticSearchService : ISemanticSearchService
{
    private static readonly string[] LowValueSegments =
    [
        "bin",
        "obj",
        ".vs",
        "node_modules",
        "dist",
        "build",
        "packages"
    ];

    private readonly IEmbeddingService _embeddingService;
    private readonly WorkspacePathResolver _pathResolver;
    private readonly ConcurrentDictionary<string, Task<WorkspaceSemanticIndex>> _indexCache = new(StringComparer.OrdinalIgnoreCase);

    public LocalSemanticSearchService(
        string workspaceRoot,
        IEmbeddingService embeddingService)
    {
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _pathResolver = new WorkspacePathResolver(workspaceRoot);
    }

    public async Task<IReadOnlyCollection<SemanticSearchResult>> SearchAsync(
        string query,
        string searchRoot,
        string filePattern,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || maxResults < 1)
        {
            return Array.Empty<SemanticSearchResult>();
        }

        var index = await GetOrBuildIndexAsync(cancellationToken);
        var resolvedSearchRoot = _pathResolver.ResolvePath(searchRoot);
        var queryEmbedding = (await _embeddingService.GenerateEmbeddingsAsync([query], cancellationToken))[0];

        var results = index.Entries
            .Where(entry => MatchesSearchRoot(entry.FullPath, resolvedSearchRoot))
            .Where(entry => FileSystemName.MatchesSimpleExpression(filePattern, Path.GetFileName(entry.FullPath), ignoreCase: true))
            .Select(entry => new
            {
                Entry = entry,
                Similarity = CosineSimilarity(queryEmbedding, entry.Embedding)
            })
            .Where(candidate => candidate.Similarity > 0.12d)
            .OrderByDescending(candidate => candidate.Similarity)
            .ThenBy(candidate => candidate.Entry.Chunk.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Entry.Chunk.StartLine)
            .Take(maxResults)
            .Select(candidate => new SemanticSearchResult(
                candidate.Entry.Chunk.Path,
                candidate.Entry.Chunk.Text,
                candidate.Similarity,
                candidate.Entry.Chunk.StartLine,
                candidate.Entry.Chunk.EndLine,
                candidate.Entry.Chunk.ClassName,
                candidate.Entry.Chunk.MethodName))
            .ToArray();

        return results;
    }

    private Task<WorkspaceSemanticIndex> GetOrBuildIndexAsync(CancellationToken cancellationToken)
    {
        return _indexCache.GetOrAdd(
            _pathResolver.WorkspaceRoot,
            _ => BuildIndexAsync(cancellationToken));
    }

    private async Task<WorkspaceSemanticIndex> BuildIndexAsync(CancellationToken cancellationToken)
    {
        var chunks = new List<SemanticCodeChunk>();
        foreach (var filePath in Directory
            .EnumerateFiles(_pathResolver.WorkspaceRoot, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsLowValuePath(filePath))
            {
                continue;
            }

            if (!TextFileDetector.IsTextFile(filePath))
            {
                continue;
            }

            var relativePath = _pathResolver.ToRelativePath(filePath);
            var content = await File.ReadAllTextAsync(filePath, cancellationToken);
            chunks.AddRange(CodeChunker.Chunk(relativePath, content));
        }

        if (chunks.Count == 0)
        {
            return new WorkspaceSemanticIndex(Array.Empty<IndexedChunk>());
        }

        var embeddings = await _embeddingService.GenerateEmbeddingsAsync(
            chunks.Select(chunk => BuildEmbeddingText(chunk)).ToArray(),
            cancellationToken);

        var entries = chunks
            .Zip(embeddings, (chunk, embedding) => new IndexedChunk(
                chunk,
                _pathResolver.ResolvePath(chunk.Path),
                embedding))
            .ToArray();

        return new WorkspaceSemanticIndex(entries);
    }

    private static string BuildEmbeddingText(SemanticCodeChunk chunk)
    {
        return string.Join(
            Environment.NewLine,
            new[]
            {
                chunk.Path,
                chunk.ClassName,
                chunk.MethodName,
                chunk.Text
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static bool MatchesSearchRoot(string fullPath, string searchRoot)
    {
        if (File.Exists(searchRoot))
        {
            return string.Equals(fullPath, searchRoot, StringComparison.OrdinalIgnoreCase);
        }

        return fullPath.Equals(searchRoot, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(searchRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static double CosineSimilarity(float[] left, float[] right)
    {
        if (left.Length != right.Length || left.Length == 0)
        {
            return 0d;
        }

        double dot = 0;
        for (var index = 0; index < left.Length; index++)
        {
            dot += left[index] * right[index];
        }

        return dot;
    }

    private static bool IsLowValuePath(string fullPath)
    {
        var normalized = fullPath.Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(segment => LowValueSegments.Contains(segment, StringComparer.OrdinalIgnoreCase));
    }

    private sealed record IndexedChunk(
        SemanticCodeChunk Chunk,
        string FullPath,
        float[] Embedding);

    private sealed record WorkspaceSemanticIndex(
        IReadOnlyCollection<IndexedChunk> Entries);
}
