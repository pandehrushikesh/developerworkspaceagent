using System.Text.Json;
using ProjectLens.Application;
using ProjectLens.Application.Abstractions;
using ProjectLens.Domain;
using ProjectLens.Infrastructure.OpenAI;
using ProjectLens.Infrastructure.Tools;

var settingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
var settings = HostSettingsLoader.Load(settingsPath);

IModelClient? modelClient = null;
if (settings.OpenAI.IsConfigured)
{
    modelClient = new OpenAiModelClient(settings.OpenAI);
}

var orchestrator = new AgentOrchestrator(
    workspacePath => new ITool[]
    {
        new ListFilesTool(workspacePath),
        new ReadFileTool(workspacePath)
    },
    modelClient,
    new AgentOrchestratorOptions
    {
        MaxIterations = settings.OpenAI.MaxIterations
    });

Console.WriteLine("ProjectLens host is ready.");
Console.WriteLine(modelClient is null
    ? "Model client is not configured. Rule-based orchestration fallback will be used."
    : $"OpenAI model client configured for model '{settings.OpenAI.Model}'.");
Console.WriteLine($"Loaded settings from {settingsPath}.");
_ = orchestrator;

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
