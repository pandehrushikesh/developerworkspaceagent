namespace ProjectLens.Infrastructure.OpenAI;

public sealed record OpenAiModelClientOptions
{
    public string? ApiKey { get; init; }

    public string? Model { get; init; }

    public string? BaseUrl { get; init; }

    public int MaxIterations { get; init; } = 8;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(Model);
}
