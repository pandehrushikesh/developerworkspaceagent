using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ProjectLens.Application.Abstractions;

namespace ProjectLens.Infrastructure.OpenAI;

public sealed class OpenAiEmbeddingService : IEmbeddingService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly OpenAiModelClientOptions _options;

    public OpenAiEmbeddingService(OpenAiModelClientOptions options, HttpClient? httpClient = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (!_options.IsEmbeddingConfigured)
        {
            throw new InvalidOperationException("OpenAI embedding service requires both ApiKey and EmbeddingModel.");
        }

        _httpClient = httpClient ?? new HttpClient();
        _httpClient.BaseAddress ??= new Uri(OpenAiModelClientOptions.NormalizeBaseUrl(_options.BaseUrl), UriKind.Absolute);

        if (_httpClient.DefaultRequestHeaders.Authorization is null)
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }
    }

    public async Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Count == 0)
        {
            return Array.Empty<float[]>();
        }

        var payload = new
        {
            model = _options.EmbeddingModel,
            input = inputs
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, "embeddings")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload, SerializerOptions),
                Encoding.UTF8,
                "application/json")
        };

        using var response = await _httpClient.SendAsync(message, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"OpenAI Embeddings API request failed with status {(int)response.StatusCode} {response.ReasonPhrase}.{Environment.NewLine}" +
                $"Response body:{Environment.NewLine}{responseBody}");
        }

        using var document = JsonDocument.Parse(responseBody);
        return document.RootElement
            .GetProperty("data")
            .EnumerateArray()
            .OrderBy(item => item.GetProperty("index").GetInt32())
            .Select(item => item.GetProperty("embedding").EnumerateArray().Select(value => value.GetSingle()).ToArray())
            .ToArray();
    }
}
