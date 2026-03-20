namespace ProjectLens.Infrastructure.OpenAI;

public sealed record OpenAiModelClientOptions
{
    public string? ApiKey { get; init; }

    public string? Model { get; init; }

    public string? EmbeddingModel { get; init; }

    public string? BaseUrl { get; init; }

    public int MaxIterations { get; init; } = 8;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(Model);

    public bool IsEmbeddingConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(EmbeddingModel);

    internal static string NormalizeBaseUrl(string? baseUrl)
    {
        var candidate = string.IsNullOrWhiteSpace(baseUrl)
            ? "https://api.openai.com/v1/"
            : baseUrl.Trim();

        return candidate.EndsWith("/", StringComparison.Ordinal)
            ? candidate
            : candidate + "/";
    }
}
