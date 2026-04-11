namespace ProjectLens.Application.Abstractions;

public sealed record ModelProviderSettings
{
    public string? Provider { get; init; }

    public string? Model { get; init; }

    public string? ApiKey { get; init; }

    public string? BaseUrl { get; init; }

    public int MaxIterations { get; init; } = 8;

    public string NormalizedProvider =>
        string.IsNullOrWhiteSpace(Provider)
            ? ModelProviderNames.None
            : Provider.Trim();
}
