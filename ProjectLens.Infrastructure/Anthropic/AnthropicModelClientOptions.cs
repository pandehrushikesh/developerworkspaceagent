namespace ProjectLens.Infrastructure.Anthropic;

public sealed record AnthropicModelClientOptions
{
    public string? ApiKey { get; init; }

    public string? Model { get; init; }

    public string? BaseUrl { get; init; }

    public int MaxTokens { get; init; } = 8096;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(Model);

    internal static string NormalizeBaseUrl(string? baseUrl)
    {
        var candidate = string.IsNullOrWhiteSpace(baseUrl)
            ? "https://api.anthropic.com/v1/"
            : baseUrl.Trim();

        return candidate.EndsWith("/", StringComparison.Ordinal)
            ? candidate
            : candidate + "/";
    }
}
