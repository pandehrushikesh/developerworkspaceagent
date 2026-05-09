using ProjectLens.Application.Abstractions;
using ProjectLens.Infrastructure.OpenAI;

namespace ProjectLens.Infrastructure;

public sealed record ProjectLensSettings
{
    public ModelProviderSettings Model { get; init; } = new();

    public OpenAiModelClientOptions OpenAI { get; init; } = new();

    /// <summary>
    /// Origins allowed to call the API. Defaults to localhost:3000 (Vite dev server).
    /// Set to ["*"] only in fully trusted, isolated environments.
    /// </summary>
    public string[] AllowedOrigins { get; init; } = ["http://localhost:3000"];

    /// <summary>
    /// If non-empty, workspacePath in API requests must be within one of these roots.
    /// Leave empty to allow any existing directory (local dev only).
    /// </summary>
    public string[] AllowedWorkspaceRoots { get; init; } = [];

    public ModelProviderSettings GetModelProviderSettings()
    {
        if (!IsNoProviderSelected(Model.Provider))
        {
            return Model;
        }

        return new ModelProviderSettings
        {
            Provider = OpenAI.IsConfigured ? ModelProviderNames.OpenAI : ModelProviderNames.None,
            Model = OpenAI.Model,
            ApiKey = OpenAI.ApiKey,
            BaseUrl = OpenAI.BaseUrl,
            MaxIterations = OpenAI.MaxIterations
        };
    }

    public OpenAiModelClientOptions GetOpenAiSettings()
    {
        if (IsProvider(Model.Provider, ModelProviderNames.OpenAI))
        {
            return OpenAI with
            {
                ApiKey = string.IsNullOrWhiteSpace(Model.ApiKey) ? OpenAI.ApiKey : Model.ApiKey,
                Model = string.IsNullOrWhiteSpace(Model.Model) ? OpenAI.Model : Model.Model,
                BaseUrl = string.IsNullOrWhiteSpace(Model.BaseUrl) ? OpenAI.BaseUrl : Model.BaseUrl,
                MaxIterations = Model.MaxIterations
            };
        }

        return OpenAI;
    }

    private static bool IsNoProviderSelected(string? provider) =>
        string.IsNullOrWhiteSpace(provider) ||
        IsProvider(provider, ModelProviderNames.None);

    private static bool IsProvider(string? provider, string expectedProvider) =>
        string.Equals(provider?.Trim(), expectedProvider, StringComparison.OrdinalIgnoreCase);
}
