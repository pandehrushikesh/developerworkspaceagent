using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ProjectLens.Application.Abstractions;

namespace ProjectLens.Infrastructure.OpenAI;

public sealed class OpenAiModelClient : IModelClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly OpenAiModelClientOptions _options;

    public OpenAiModelClient(OpenAiModelClientOptions options, HttpClient? httpClient = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (!_options.IsConfigured)
        {
            throw new InvalidOperationException("OpenAI model client requires both ApiKey and Model.");
        }

        _httpClient = httpClient ?? new HttpClient();
        _httpClient.BaseAddress ??= new Uri(NormalizeBaseUrl(_options.BaseUrl), UriKind.Absolute);

        if (_httpClient.DefaultRequestHeaders.Authorization is null)
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }
    }

    public async Task<ModelResponse> GenerateAsync(
        ModelRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var payload = new
        {
            model = _options.Model,
            instructions = request.Instructions,
            input = BuildInput(request.Conversation),
            tools = BuildTools(request.AvailableTools),
            previous_response_id = string.IsNullOrWhiteSpace(request.PreviousResponseId)
                ? null
                : request.PreviousResponseId,
            store = true
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, "responses")
        {
            Content = new StringContent(
                SerializePayload(payload),
                Encoding.UTF8,
                "application/json")
        };

        using var response = await _httpClient.SendAsync(message, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw BuildApiFailure(response.StatusCode, response.ReasonPhrase, responseBody);
        }

        return ParseResponse(responseBody);
    }

    private static object[] BuildInput(IReadOnlyCollection<ModelConversationItem> conversation)
    {
        return conversation
            .Select(item => item switch
            {
                ModelTextMessage textMessage => (object)new
                {
                    role = textMessage.Role,
                    content = new[]
                    {
                        new
                        {
                            type = "input_text",
                            text = textMessage.Content
                        }
                    }
                },
                ModelToolResultMessage toolResult => (object)new
                {
                    type = "function_call_output",
                    call_id = toolResult.CallId,
                    output = toolResult.Output
                },
                _ => throw new InvalidOperationException($"Unsupported conversation item: {item.GetType().Name}")
            })
            .ToArray();
    }

    private static object[] BuildTools(IReadOnlyCollection<ModelToolDefinition> tools)
    {
        return tools
            .Select(tool => new
            {
                type = "function",
                name = tool.Name,
                description = tool.Description,
                strict = true,
                parameters = BuildToolParameters(tool.Parameters)
            })
            .ToArray();
    }

    private static object BuildToolParameters(IReadOnlyDictionary<string, string>? parameters)
    {
        var toolParameters = parameters ?? new Dictionary<string, string>();

        return new
        {
            type = "object",
            properties = toolParameters.ToDictionary(
                parameter => parameter.Key,
                parameter => (object)new
                {
                    type = "string",
                    description = parameter.Value
                }),
            required = toolParameters.Keys.ToArray(),
            additionalProperties = false
        };
    }

    private static ModelResponse ParseResponse(string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;
        var responseId = root.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String
            ? idElement.GetString()
            : null;

        var toolCalls = new List<ModelToolCall>();
        var finalAnswerParts = new List<string>();

        if (root.TryGetProperty("output", out var outputElement))
        {
            foreach (var item in outputElement.EnumerateArray())
            {
                var type = item.TryGetProperty("type", out var typeElement)
                    ? typeElement.GetString()
                    : null;

                if (string.Equals(type, "function_call", StringComparison.Ordinal))
                {
                    toolCalls.Add(ParseToolCall(item));
                    continue;
                }

                if (string.Equals(type, "message", StringComparison.Ordinal))
                {
                    finalAnswerParts.AddRange(ParseMessageText(item));
                }
            }
        }

        if (finalAnswerParts.Count == 0 &&
            root.TryGetProperty("output_text", out var outputTextElement) &&
            outputTextElement.ValueKind == JsonValueKind.String)
        {
            var outputText = outputTextElement.GetString();
            if (!string.IsNullOrWhiteSpace(outputText))
            {
                finalAnswerParts.Add(outputText);
            }
        }

        var finalAnswer = finalAnswerParts.Count == 0
            ? null
            : string.Join(Environment.NewLine, finalAnswerParts.Where(part => !string.IsNullOrWhiteSpace(part)));

        return new ModelResponse(finalAnswer, toolCalls, responseId);
    }

    private static IEnumerable<string> ParseMessageText(JsonElement messageElement)
    {
        if (!messageElement.TryGetProperty("content", out var contentElement))
        {
            yield break;
        }

        foreach (var contentItem in contentElement.EnumerateArray())
        {
            var type = contentItem.TryGetProperty("type", out var typeElement)
                ? typeElement.GetString()
                : null;

            if (type is not ("output_text" or "text"))
            {
                continue;
            }

            if (contentItem.TryGetProperty("text", out var textElement))
            {
                var text = textElement.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    yield return text;
                }
            }
        }
    }

    private static ModelToolCall ParseToolCall(JsonElement toolCallElement)
    {
        var callId = toolCallElement.GetProperty("call_id").GetString() ?? Guid.NewGuid().ToString("N");
        var toolName = toolCallElement.GetProperty("name").GetString() ?? string.Empty;
        var arguments = toolCallElement.TryGetProperty("arguments", out var argumentsElement)
            ? ParseArguments(argumentsElement.GetString())
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        return new ModelToolCall(callId, toolName, arguments);
    }

    private static IReadOnlyDictionary<string, string> ParseArguments(string? argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        using var document = JsonDocument.Parse(argumentsJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var arguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            arguments[property.Name] = ConvertJsonValueToString(property.Value);
        }

        return arguments;
    }

    private static string ConvertJsonValueToString(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Null => string.Empty,
            _ => element.GetRawText()
        };
    }

    private static string NormalizeBaseUrl(string? baseUrl)
    {
        var candidate = string.IsNullOrWhiteSpace(baseUrl)
            ? "https://api.openai.com/v1/"
            : baseUrl.Trim();

        return candidate.EndsWith("/", StringComparison.Ordinal)
            ? candidate
            : candidate + "/";
    }

    private static InvalidOperationException BuildApiFailure(
        System.Net.HttpStatusCode statusCode,
        string? reasonPhrase,
        string responseBody)
    {
        var reason = string.IsNullOrWhiteSpace(reasonPhrase)
            ? string.Empty
            : $" {reasonPhrase}";

        return new InvalidOperationException(
            $"OpenAI Responses API request failed with status {(int)statusCode}{reason}.{Environment.NewLine}" +
            $"Response body:{Environment.NewLine}{responseBody}");
    }

    private static string SerializePayload(object payload)
    {
        // Helpful during local debugging: inspect this JSON in a debugger before the request is sent.
        return JsonSerializer.Serialize(payload, SerializerOptions);
    }
}
