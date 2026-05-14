using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectLens.Api.Models;
using ProjectLens.Application;
using ProjectLens.Application.Abstractions;
using ProjectLens.Domain;
using ProjectLens.Infrastructure;
using ProjectLens.Infrastructure.OpenAI;
using ProjectLens.Infrastructure.SemanticSearch;
using ProjectLens.Infrastructure.Tools;

var builder = WebApplication.CreateBuilder(args);

var settings = builder.Configuration.Get<ProjectLensSettings>() ?? new ProjectLensSettings();

builder.Services.AddSingleton(settings);

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
    {
        var origins = settings.AllowedOrigins;
        if (origins.Length == 1 && origins[0] == "*")
        {
            policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
        }
        else
        {
            policy.WithOrigins(origins).AllowAnyMethod().AllowAnyHeader();
        }
    }));

builder.Services.AddSingleton<IAgentOrchestrator>(sp =>
{
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
                new LocalSemanticSearchService(workspacePath, embeddingService)),
            new GitLogTool(workspacePath),
            new GitBlameTool(workspacePath),
            new GitDiffTool(workspacePath)
        },
        orchestrationDependencies,
        modelClient,
        new AgentOrchestratorOptions { MaxIterations = modelSettings.MaxIterations });
});

var app = builder.Build();

app.UseCors();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", timestamp = DateTimeOffset.UtcNow }));

app.MapPost("/api/query", async (
    QueryRequest request,
    IAgentOrchestrator orchestrator,
    ProjectLensSettings apiSettings,
    CancellationToken ct) =>
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

    if (!IsWorkspaceAllowed(normalizedPath, apiSettings.AllowedWorkspaceRoots))
    {
        return Results.BadRequest(new { error = "The workspace path is not within an allowed root." });
    }

    var agentRequest = new AgentRequest(request.Prompt, normalizedPath);
    var agentResponse = await orchestrator.ProcessAsync(agentRequest, ct);

    var evidenceItems = agentResponse.EvidenceItems?
        .Select(e => new EvidenceItemDto(e.ToolName, e.SourceId, e.Content, e.Kind, e.IsPartial, e.Confidence))
        .ToArray();

    var finalAssessment = agentResponse.FinalAssessment is { } a
        ? new EvidenceAssessmentDto(a.IsSufficient, a.CoverageScore, a.ConfidenceScore, a.Reason, a.MissingAreas)
        : null;

    var response = new QueryResponse(
        agentResponse.Success,
        agentResponse.Output,
        agentResponse.ErrorMessage,
        agentResponse.ExecutionSteps?
            .Select(s => new ExecutionStepDto(s.Description, s.Success))
            .ToArray() ?? [],
        agentResponse.ToolResults?
            .Select(r => new ToolResultDto(r.ToolName, r.Success, r.ErrorMessage))
            .ToArray() ?? [],
        evidenceItems,
        finalAssessment);

    return Results.Ok(response);
});

app.MapPost("/api/query/stream", async (
    QueryRequest request,
    IAgentOrchestrator orchestrator,
    ProjectLensSettings apiSettings,
    HttpContext httpContext,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.WorkspacePath))
    {
        httpContext.Response.StatusCode = 400;
        await httpContext.Response.WriteAsJsonAsync(new { error = "workspacePath is required." }, ct);
        return;
    }

    if (string.IsNullOrWhiteSpace(request.Prompt))
    {
        httpContext.Response.StatusCode = 400;
        await httpContext.Response.WriteAsJsonAsync(new { error = "prompt is required." }, ct);
        return;
    }

    var normalizedPath = Path.GetFullPath(request.WorkspacePath);

    if (!Directory.Exists(normalizedPath))
    {
        httpContext.Response.StatusCode = 400;
        await httpContext.Response.WriteAsJsonAsync(new { error = "The workspace path does not exist." }, ct);
        return;
    }

    if (!IsWorkspaceAllowed(normalizedPath, apiSettings.AllowedWorkspaceRoots))
    {
        httpContext.Response.StatusCode = 400;
        await httpContext.Response.WriteAsJsonAsync(new { error = "The workspace path is not within an allowed root." }, ct);
        return;
    }

    httpContext.Response.ContentType = "text/event-stream";
    httpContext.Response.Headers.CacheControl = "no-cache";
    httpContext.Response.Headers["X-Accel-Buffering"] = "no";
    await httpContext.Response.Body.FlushAsync(ct);

    var sseOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    async Task WriteSseAsync(object payload)
    {
        var json = JsonSerializer.Serialize(payload, sseOptions);
        var line = $"data: {json}\n\n";
        await httpContext.Response.Body.WriteAsync(Encoding.UTF8.GetBytes(line), ct);
        await httpContext.Response.Body.FlushAsync(ct);
    }

    var progress = new Progress<AgentProgressEvent>(evt =>
    {
        _ = evt switch
        {
            StepProgressEvent step =>
                WriteSseAsync(new { type = "step", description = step.Description, success = step.Success }),
            ToolResultProgressEvent tool =>
                WriteSseAsync(new { type = "tool_result", toolName = tool.ToolName, success = tool.Success, errorMessage = tool.ErrorMessage }),
            AnswerProgressEvent answer =>
                WriteSseAsync(new { type = "answer", text = answer.Text }),
            _ => Task.CompletedTask
        };
    });

    try
    {
        var agentRequest = new AgentRequest(request.Prompt, normalizedPath);
        var agentResponse = await orchestrator.ProcessAsync(agentRequest, progress, ct);

        var evidenceItems = agentResponse.EvidenceItems?
            .Select(e => new EvidenceItemDto(e.ToolName, e.SourceId, e.Content, e.Kind, e.IsPartial, e.Confidence))
            .ToArray();

        var finalAssessment = agentResponse.FinalAssessment is { } a
            ? new EvidenceAssessmentDto(a.IsSufficient, a.CoverageScore, a.ConfidenceScore, a.Reason, a.MissingAreas)
            : null;

        await WriteSseAsync(new
        {
            type = "done",
            success = agentResponse.Success,
            output = agentResponse.Output,
            errorMessage = agentResponse.ErrorMessage,
            executionSteps = agentResponse.ExecutionSteps?
                .Select(s => new { description = s.Description, success = s.Success })
                .ToArray() ?? Array.Empty<object>(),
            toolResults = agentResponse.ToolResults?
                .Select(r => new { toolName = r.ToolName, success = r.Success, errorMessage = r.ErrorMessage })
                .ToArray() ?? Array.Empty<object>(),
            evidenceItems,
            finalAssessment
        });
    }
    catch (OperationCanceledException)
    {
        // client disconnected — nothing to do
    }
    catch (Exception ex)
    {
        await WriteSseAsync(new { type = "error", message = ex.Message });
    }
});

app.Run();

static bool IsWorkspaceAllowed(string normalizedPath, string[] allowedRoots)
{
    if (allowedRoots.Length == 0)
    {
        return true;
    }

    return allowedRoots.Any(root =>
    {
        var normalizedRoot = Path.GetFullPath(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return normalizedPath.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    });
}
