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
    var settings = builder.Configuration.Get<ProjectLensSettings>() ?? new ProjectLensSettings();
    var modelSettings = settings.GetModelProviderSettings();
    var openAiSettings = settings.GetOpenAiSettings();

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
