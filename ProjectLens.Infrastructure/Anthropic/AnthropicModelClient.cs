using System.Text;
using System.Text.Json;
using ProjectLens.Application.Abstractions;

namespace ProjectLens.Infrastructure.Anthropic;

public sealed class AnthropicModelClient : IModelClient
{
    private const string AnthropicVersion = "2023-06-01";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly AnthropicModelClientOptions _options;

    public AnthropicModelClient(AnthropicModelClientOptions options, HttpClient? httpClient = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (!_options.IsConfigured)
        {
            throw new InvalidOperationException("Anthropic model client requires both ApiKey and Model.");
        }

        _httpClient = httpClient ?? new HttpClient();
        _httpClient.BaseAddress ??= new Uri(AnthropicModelClientOptions.NormalizeBaseUrl(_options.BaseUrl));

        if (!_httpClient.DefaultRequestHeaders.Contains("x-api-key"))
        {
            _httpClient.DefaultRequestHeaders.Add("x-api-key", _options.ApiKey!);
        }

        if (!_httpClient.DefaultRequestHeaders.Contains("anthropic-version"))
        {
            _httpClient.DefaultRequestHeaders.Add("anthropic-version", AnthropicVersion);
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
            max_tokens = _options.MaxTokens,
            system = request.Instructions,
            messages = BuildMessages(request.Conversation),
            tools = BuildTools(request.AvailableTools)
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, "messages")
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
            throw BuildApiFailure(response.StatusCode, response.ReasonPhrase, responseBody);
        }

        return ParseResponse(responseBody);
    }

    private static object[] BuildMessages(IReadOnlyCollection<ModelConversationItem> conversation)
    {
        var messages = new List<object>();
        var items = conversation.ToArray();
        var i = 0;

        while (i < items.Length)
        {
            switch (items[i])
            {
                case ModelTextMessage textMsg:
                    messages.Add(new
                    {
                        role = textMsg.Role,
                        content = new[] { new { type = "text", text = textMsg.Content } }
                    });
                    i++;
                    break;

                case ModelToolCallMessage:
                    var toolUseBlocks = new List<object>();
                    while (i < items.Length && items[i] is ModelToolCallMessage tcm)
                    {
                        toolUseBlocks.Add(new
                        {
                            type = "tool_use",
                            id = tcm.ToolCall.CallId,
                            name = tcm.ToolCall.ToolName,
                            input = (object)tcm.ToolCall.Arguments
                        });
                        i++;
                    }
                    messages.Add(new { role = "assistant", content = (object)toolUseBlocks });
                    break;

                case ModelToolResultMessage:
                    var toolResultBlocks = new List<object>();
                    while (i < items.Length && items[i] is ModelToolResultMessage trm)
                    {
                        toolResultBlocks.Add(new
                        {
                            type = "tool_result",
                            tool_use_id = trm.CallId,
                            content = trm.Output
                        });
                        i++;
                    }
                    messages.Add(new { role = "user", content = (object)toolResultBlocks });
                    break;

                default:
                    i++;
                    break;
            }
        }

        return messages.ToArray();
    }

    private static object[] BuildTools(IReadOnlyCollection<ModelToolDefinition> tools)
    {
        return tools
            .Select(tool => new
            {
                name = tool.Name,
                description = tool.Description,
                input_schema = BuildInputSchema(tool.Parameters)
            })
            .ToArray();
    }

    private static object BuildInputSchema(IReadOnlyDictionary<string, string>? parameters)
    {
        var toolParameters = parameters ?? new Dictionary<string, string>();

        return new
        {
            type = "object",
            properties = toolParameters.ToDictionary(
                p => p.Key,
                p => (object)new { type = "string", description = p.Value }),
            required = toolParameters.Keys.ToArray()
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
        var textParts = new List<string>();

        if (root.TryGetProperty("content", out var contentElement))
        {
            foreach (var item in contentElement.EnumerateArray())
            {
                var type = item.TryGetProperty("type", out var typeElement)
                    ? typeElement.GetString()
                    : null;

                if (string.Equals(type, "tool_use", StringComparison.Ordinal))
                {
                    toolCalls.Add(ParseToolCall(item));
                    continue;
                }

                if (string.Equals(type, "text", StringComparison.Ordinal) &&
                    item.TryGetProperty("text", out var textElement))
                {
                    var text = textElement.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        textParts.Add(text);
                    }
                }
            }
        }

        var finalAnswer = textParts.Count == 0
            ? null
            : string.Join(Environment.NewLine, textParts.Where(p => !string.IsNullOrWhiteSpace(p)));

        return new ModelResponse(finalAnswer, toolCalls, responseId);
    }

    private static ModelToolCall ParseToolCall(JsonElement toolUseElement)
    {
        var callId = toolUseElement.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
            ? idEl.GetString() ?? Guid.NewGuid().ToString("N")
            : Guid.NewGuid().ToString("N");

        var name = toolUseElement.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String
            ? nameEl.GetString() ?? string.Empty
            : string.Empty;

        var arguments = toolUseElement.TryGetProperty("input", out var inputEl)
            ? ParseInputArguments(inputEl)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        return new ModelToolCall(callId, name, arguments);
    }

    private static IReadOnlyDictionary<string, string> ParseInputArguments(JsonElement inputElement)
    {
        if (inputElement.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in inputElement.EnumerateObject())
        {
            args[prop.Name] = prop.Value.ValueKind switch
            {
                JsonValueKind.String => prop.Value.GetString() ?? string.Empty,
                JsonValueKind.Null => string.Empty,
                _ => prop.Value.GetRawText()
            };
        }

        return args;
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
            $"Anthropic Messages API request failed with status {(int)statusCode}{reason}.{Environment.NewLine}" +
            $"Response body:{Environment.NewLine}{responseBody}");
    }
}
