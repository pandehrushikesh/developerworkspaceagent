using ProjectLens.Api.Models;
using ProjectLens.Application;
using ProjectLens.Application.Abstractions;
using ProjectLens.Domain;
using ProjectLens.Infrastructure;
using ProjectLens.Infrastructure.OpenAI;
using ProjectLens.Infrastructure.SemanticSearch;
using ProjectLens.Infrastructure.Tools;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

builder.Services.AddSingleton<IAgentOrchestrator>(sp =>
{
    var modelSettings = ResolveModelSettings(builder.Configuration);
    var openAiSettings = ResolveOpenAiSettings(builder.Configuration, modelSettings);

    IModelClientFactory factory = new DefaultModelClientFactory();
    IModelClient? modelClient = factory.Create(modelSettings);

    IAgentSessionStore sessionStore = new FileBasedAgentSessionStore(AppContext.BaseDirectory);
    IEvidenceQualityEvaluator evidenceQualityEvaluator = new RuleBasedEvidenceQualityEvaluator();
    IEmbeddingService embeddingService = openAiSettings.IsEmbeddingConfigured
        ? new OpenAiEmbeddingService(openAiSettings)
        : new DeterministicEmbeddingService();
    IFileCompressor fileCompressor = new RuleBasedFileCompressor();
    IPromptClarifier promptClarifier = new RuleBasedPromptClarifier();
    ISessionSummarizer sessionSummarizer = new RuleBasedSessionSummarizer(evidenceQualityEvaluator);

    var orchestrationDependencies = AgentOrchestrationDependencies.CreateDefault(
        sessionStore,
        fileCompressor,
        sessionSummarizer,
        evidenceQualityEvaluator,
        promptClarifier);

    return new AgentOrchestrator(
        workspacePath => new ITool[]
        {
            new ListFilesTool(workspacePath),
            new ReadFileTool(workspacePath),
            new WriteFileTool(workspacePath),
            new EditFileTool(workspacePath),
            new SearchFilesTool(
                workspacePath,
                evidenceQualityEvaluator,
                new LocalSemanticSearchService(workspacePath, embeddingService))
        },
        orchestrationDependencies,
        modelClient,
        new AgentOrchestratorOptions { MaxIterations = modelSettings.MaxIterations });
});

var app = builder.Build();

app.UseCors();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", timestamp = DateTimeOffset.UtcNow }));

app.MapPost("/api/query", async (QueryRequest request, IAgentOrchestrator orchestrator, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.WorkspacePath))
    {
        return Results.BadRequest(new { error = "workspacePath is required." });
    }

    if (string.IsNullOrWhiteSpace(request.Prompt))
    {
        return Results.BadRequest(new { error = "prompt is required." });
    }

    var normalizedPath = Path.GetFullPath(request.WorkspacePath);
    if (!Directory.Exists(normalizedPath))
    {
        return Results.BadRequest(new { error = "The workspace path does not exist." });
    }

    var agentRequest = new AgentRequest(request.Prompt, normalizedPath);
    var agentResponse = await orchestrator.ProcessAsync(agentRequest, ct);

    var response = new QueryResponse(
        agentResponse.Success,
        agentResponse.Output,
        agentResponse.ErrorMessage,
        agentResponse.ExecutionSteps?
            .Select(s => new ExecutionStepDto(s.Description, s.Success))
            .ToArray() ?? [],
        agentResponse.ToolResults?
            .Select(r => new ToolResultDto(r.ToolName, r.Success, r.ErrorMessage))
            .ToArray() ?? []);

    return Results.Ok(response);
});

app.Run();

static ModelProviderSettings ResolveModelSettings(IConfiguration config)
{
    var model = config.GetSection("Model").Get<ModelProviderSettings>() ?? new ModelProviderSettings();

    if (!IsNoProvider(model.Provider))
    {
        return model;
    }

    var openAi = config.GetSection("OpenAI").Get<OpenAiModelClientOptions>() ?? new OpenAiModelClientOptions();
    return openAi.IsConfigured
        ? new ModelProviderSettings
        {
            Provider = ModelProviderNames.OpenAI,
            Model = openAi.Model,
            ApiKey = openAi.ApiKey,
            BaseUrl = openAi.BaseUrl,
            MaxIterations = openAi.MaxIterations
        }
        : new ModelProviderSettings { Provider = ModelProviderNames.None };
}

static OpenAiModelClientOptions ResolveOpenAiSettings(IConfiguration config, ModelProviderSettings modelSettings)
{
    var openAi = config.GetSection("OpenAI").Get<OpenAiModelClientOptions>() ?? new OpenAiModelClientOptions();

    if (!string.Equals(modelSettings.NormalizedProvider, ModelProviderNames.OpenAI, StringComparison.OrdinalIgnoreCase))
    {
        return openAi;
    }

    return openAi with
    {
        ApiKey = string.IsNullOrWhiteSpace(modelSettings.ApiKey) ? openAi.ApiKey : modelSettings.ApiKey,
        Model = string.IsNullOrWhiteSpace(modelSettings.Model) ? openAi.Model : modelSettings.Model,
        BaseUrl = string.IsNullOrWhiteSpace(modelSettings.BaseUrl) ? openAi.BaseUrl : modelSettings.BaseUrl,
        MaxIterations = modelSettings.MaxIterations
    };
}

static bool IsNoProvider(string? provider) =>
    string.IsNullOrWhiteSpace(provider) ||
    string.Equals(provider.Trim(), ModelProviderNames.None, StringComparison.OrdinalIgnoreCase);
