using ProjectLens.Application.Abstractions;
using ProjectLens.Infrastructure.Anthropic;
using ProjectLens.Infrastructure.Fake;
using ProjectLens.Infrastructure.OpenAI;

namespace ProjectLens.Infrastructure;

public sealed class DefaultModelClientFactory : IModelClientFactory
{
    private readonly IReadOnlyDictionary<string, Func<ModelProviderSettings, IModelClient?>> _clientCreators;

    public DefaultModelClientFactory()
    {
        _clientCreators = new Dictionary<string, Func<ModelProviderSettings, IModelClient?>>(
            StringComparer.OrdinalIgnoreCase)
        {
            [ModelProviderNames.None] = _ => null,
            [ModelProviderNames.Fake] = settings => new FakeModelClient(settings.Model),
            [ModelProviderNames.OpenAI] = CreateOpenAiClient,
            [ModelProviderNames.Anthropic] = CreateAnthropicClient
        };
    }

    public IModelClient? Create(ModelProviderSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var provider = settings.NormalizedProvider;
        if (!_clientCreators.TryGetValue(provider, out var createClient))
        {
            throw new InvalidOperationException($"Unsupported model provider '{provider}'.");
        }

        return createClient(settings);
    }

    private static IModelClient? CreateOpenAiClient(ModelProviderSettings settings)
    {
        var options = new OpenAiModelClientOptions
        {
            ApiKey = settings.ApiKey,
            Model = settings.Model,
            BaseUrl = settings.BaseUrl
        };

        return options.IsConfigured
            ? new OpenAiModelClient(options)
            : null;
    }

    private static IModelClient? CreateAnthropicClient(ModelProviderSettings settings)
    {
        var options = new AnthropicModelClientOptions
        {
            ApiKey = settings.ApiKey,
            Model = settings.Model,
            BaseUrl = settings.BaseUrl
        };

        return options.IsConfigured
            ? new AnthropicModelClient(options)
            : null;
    }
}
