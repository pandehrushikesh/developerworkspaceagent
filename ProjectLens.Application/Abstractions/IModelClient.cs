namespace ProjectLens.Application.Abstractions;

public interface IModelClient
{
    Task<ModelResponse> GenerateAsync(
        ModelRequest request,
        CancellationToken cancellationToken = default);
}
