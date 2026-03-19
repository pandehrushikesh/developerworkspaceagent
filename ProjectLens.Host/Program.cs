using System.Text.Json;
using ProjectLens.Application;
using ProjectLens.Application.Abstractions;
using ProjectLens.Domain;
using ProjectLens.Infrastructure;
using ProjectLens.Infrastructure.OpenAI;
using ProjectLens.Infrastructure.Tools;

try
{
    var settingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
    var settings = HostSettingsLoader.Load(settingsPath);

    IModelClient? modelClient = null;
    if (settings.OpenAI.IsConfigured)
    {
        modelClient = new OpenAiModelClient(settings.OpenAI);
    }

    IAgentSessionStore sessionStore = new FileBasedAgentSessionStore(AppContext.BaseDirectory);
    IFileCompressor fileCompressor = new RuleBasedFileCompressor();
    ISessionSummarizer sessionSummarizer = new RuleBasedSessionSummarizer();

    var orchestrator = new AgentOrchestrator(
        workspacePath => new ITool[]
        {
            new ListFilesTool(workspacePath),
            new ReadFileTool(workspacePath),
            new SearchFilesTool(workspacePath)
        },
        modelClient,
        new AgentOrchestratorOptions
        {
            MaxIterations = settings.OpenAI.MaxIterations
        },
        sessionStore,
        fileCompressor,
        sessionSummarizer);

    Console.WriteLine("ProjectLens host is ready.");
    Console.WriteLine(modelClient is null
        ? "Model client is not configured. Rule-based orchestration fallback will be used."
        : $"OpenAI model client configured for model '{settings.OpenAI.Model}'.");
    Console.WriteLine($"Loaded settings from {settingsPath}.");

    var workspacePath = ReadRequiredInput("Enter workspace path:");
    if (workspacePath is null)
    {
        Console.WriteLine("A workspace path is required. Exiting.");
        return;
    }

    var normalizedWorkspacePath = Path.GetFullPath(workspacePath);
    if (!Directory.Exists(normalizedWorkspacePath))
    {
        Console.WriteLine("The workspace path does not exist. Exiting.");
        return;
    }

    Console.WriteLine();
    Console.WriteLine($"Analyzing workspace: {normalizedWorkspacePath}");
    Console.WriteLine("Enter prompts for this workspace. Submit an empty prompt to exit.");

    while (true)
    {
        var userPrompt = ReadRequiredInput("Enter your prompt:");
        if (userPrompt is null)
        {
            Console.WriteLine("Session ended.");
            break;
        }

        var request = new AgentRequest(userPrompt, normalizedWorkspacePath);
        var response = await orchestrator.ProcessAsync(request);

        PrintResponse(response);
        Console.WriteLine();
    }
}
catch (Exception exception)
{
    Console.WriteLine($"An unexpected error occurred: {exception.Message}");
}

static string? ReadRequiredInput(string prompt)
{
    Console.WriteLine(prompt);
    return Console.ReadLine()?.Trim();
}

static void PrintResponse(AgentResponse response)
{
    Console.WriteLine();
    Console.WriteLine("=== FINAL ANSWER ===");

    if (response.Success)
    {
        Console.WriteLine(string.IsNullOrWhiteSpace(response.Output)
            ? "(No output returned.)"
            : response.Output);
    }
    else
    {
        Console.WriteLine(string.IsNullOrWhiteSpace(response.ErrorMessage)
            ? "The agent did not complete successfully."
            : response.ErrorMessage);
    }

    Console.WriteLine();
    Console.WriteLine("=== EXECUTION STEPS ===");

    if (response.ExecutionSteps is { Count: > 0 })
    {
        foreach (var step in response.ExecutionSteps)
        {
            Console.WriteLine($"[{(step.Success ? "OK" : "FAIL")}] {step.Description}");
        }
    }
    else
    {
        Console.WriteLine("(No execution steps recorded.)");
    }

    if (response.ToolResults is not { Count: > 0 })
    {
        return;
    }

    Console.WriteLine();
    Console.WriteLine("=== TOOL RESULTS ===");

    foreach (var toolResult in response.ToolResults)
    {
        var status = toolResult.Success ? "SUCCESS" : "FAILURE";
        var detail = toolResult.Success
            ? string.Empty
            : $": {toolResult.ErrorMessage ?? "Unknown tool error."}";

        Console.WriteLine($"{toolResult.ToolName}: {status}{detail}");
    }
}

internal static class HostSettingsLoader
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static HostSettings Load(string settingsPath)
    {
        if (!File.Exists(settingsPath))
        {
            return new HostSettings();
        }

        var json = File.ReadAllText(settingsPath);
        return JsonSerializer.Deserialize<HostSettings>(json, SerializerOptions) ?? new HostSettings();
    }
}

internal sealed record HostSettings
{
    public OpenAiModelClientOptions OpenAI { get; init; } = new();
}
