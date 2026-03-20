namespace ProjectLens.Application.Abstractions;

public interface ISemanticSearchService
{
    Task<IReadOnlyCollection<SemanticSearchResult>> SearchAsync(
        string query,
        string searchRoot,
        string filePattern,
        int maxResults,
        CancellationToken cancellationToken = default);
}
