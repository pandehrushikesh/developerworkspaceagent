using ProjectLens.Application.Abstractions;

namespace ProjectLens.Infrastructure.Fake;

public sealed class FakeModelClient : IModelClient
{
    private readonly string _modelName;

    public FakeModelClient(string? modelName = null)
    {
        _modelName = string.IsNullOrWhiteSpace(modelName)
            ? "fake"
            : modelName.Trim();
    }

    public Task<ModelResponse> GenerateAsync(
        ModelRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var lastUserPrompt = request.Conversation
            .OfType<ModelTextMessage>()
            .LastOrDefault(message => string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase))
            ?.Content
            ?.Trim();

        var promptSummary = string.IsNullOrWhiteSpace(lastUserPrompt)
            ? "No user prompt was supplied."
            : lastUserPrompt;

        return Task.FromResult(new ModelResponse(
            $"Fake model '{_modelName}' received: {promptSummary}",
            ResponseId: $"fake-{_modelName}"));
    }
}
