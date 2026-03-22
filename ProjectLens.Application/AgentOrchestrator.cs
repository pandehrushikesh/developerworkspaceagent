using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using ProjectLens.Application.Abstractions;
using ProjectLens.Domain;

namespace ProjectLens.Application;

public sealed class AgentOrchestrator : IAgentOrchestrator
{
    private const string ListFilesToolName = "list_files";
    private const string ReadFileToolName = "read_file";
    private const string SearchFilesToolName = "search_files";
    private const string SessionIdContextKey = "sessionId";

    private readonly IFileCompressor? _fileCompressor;
    private readonly IEvidenceQualityEvaluator? _evidenceQualityEvaluator;
    private readonly IModelClient? _modelClient;
    private readonly AgentOrchestratorOptions _options;
    private readonly IPromptClarifier _promptClarifier;
    private readonly IAgentSessionStore? _sessionStore;
    private readonly ISessionSummarizer? _sessionSummarizer;
    private readonly Func<string, IReadOnlyDictionary<string, ITool>> _toolFactory;

    public AgentOrchestrator(
        IEnumerable<ITool> tools,
        IModelClient? modelClient = null,
        AgentOrchestratorOptions? options = null,
        IAgentSessionStore? sessionStore = null,
        IFileCompressor? fileCompressor = null,
        ISessionSummarizer? sessionSummarizer = null,
        IEvidenceQualityEvaluator? evidenceQualityEvaluator = null,
        IPromptClarifier? promptClarifier = null)
        : this(_ => tools, modelClient, options, sessionStore, fileCompressor, sessionSummarizer, evidenceQualityEvaluator, promptClarifier)
    {
    }

    public AgentOrchestrator(
        Func<string, IEnumerable<ITool>> toolFactory,
        IModelClient? modelClient = null,
        AgentOrchestratorOptions? options = null,
        IAgentSessionStore? sessionStore = null,
        IFileCompressor? fileCompressor = null,
        ISessionSummarizer? sessionSummarizer = null,
        IEvidenceQualityEvaluator? evidenceQualityEvaluator = null,
        IPromptClarifier? promptClarifier = null)
    {
        ArgumentNullException.ThrowIfNull(toolFactory);

        _evidenceQualityEvaluator = evidenceQualityEvaluator;
        _fileCompressor = fileCompressor;
        _modelClient = modelClient;
        _options = options ?? new AgentOrchestratorOptions();
        _promptClarifier = promptClarifier ?? new RuleBasedPromptClarifier();
        _sessionStore = sessionStore;
        _sessionSummarizer = sessionSummarizer;
        if (_options.MaxIterations < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaxIterations must be greater than 0.");
        }

        _toolFactory = workspacePath =>
        {
            var tools = toolFactory(workspacePath)
                ?? throw new InvalidOperationException("The tool factory returned no tools.");

            return tools.ToDictionary(
                tool => tool.Definition.Name,
                StringComparer.OrdinalIgnoreCase);
        };
    }

    public Task<AgentResponse> ProcessAsync(
        AgentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return _modelClient is null
            ? ProcessRuleBasedAsync(request, cancellationToken)
            : ProcessModelDrivenAsync(request, cancellationToken);
    }

    private async Task<AgentResponse> ProcessModelDrivenAsync(
        AgentRequest request,
        CancellationToken cancellationToken)
    {
        var steps = new List<AgentExecutionStep>();
        var toolResults = new List<ToolExecutionResult>();

        if (!TryValidateRequest(request, steps, toolResults, out var invalidResponse))
        {
            return invalidResponse!;
        }

        var executionContext = await InitializeModelExecutionAsync(request, steps, cancellationToken);
        var tools = executionContext.Tools;
        var registeredTools = executionContext.RegisteredTools;
        var sessionState = executionContext.SessionState;

        if (TryCreateClarificationResponse(request, sessionState, steps, toolResults, out var clarificationResponse))
        {
            return clarificationResponse!;
        }

        var conversation = new List<ModelConversationItem>
        {
            new ModelTextMessage("user", request.UserPrompt)
        };
        var executedToolCalls = new HashSet<string>(StringComparer.Ordinal);
        AggregatedEvidenceContext? aggregatedEvidenceContext = null;
        var hasPendingWeakSearchEvidence = false;
        string? weakSearchRecoveryGuidance = null;
        string? previousResponseId = null;

        for (var iteration = 1; iteration <= _options.MaxIterations; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            steps.Add(new AgentExecutionStep($"Calling model for iteration {iteration}."));

            var modelRequest = BuildModelRequest(conversation, registeredTools, sessionState, previousResponseId);
            var modelResponse = await _modelClient!.GenerateAsync(modelRequest, cancellationToken);
            previousResponseId = modelResponse.ResponseId;

            var toolCalls = modelResponse.ToolCalls?.Where(call => !string.IsNullOrWhiteSpace(call.ToolName)).ToArray()
                ?? Array.Empty<ModelToolCall>();

            if (TryHandleFinalAnswer(
                    modelResponse.FinalAnswer,
                    toolCalls,
                    hasPendingWeakSearchEvidence,
                    weakSearchRecoveryGuidance,
                    aggregatedEvidenceContext,
                    request.UserPrompt,
                    iteration,
                    steps,
                    toolResults,
                    out var finalResponse,
                    out var followUpPrompt))
            {
                if (finalResponse is not null)
                {
                    return finalResponse;
                }

                conversation.Add(new ModelTextMessage("user", followUpPrompt!));
                continue;
            }

            if (toolCalls.Length == 0)
            {
                steps.Add(new AgentExecutionStep("Model response did not include a final answer or tool call.", false));
                return Failure(
                    "The model did not return a final answer or a tool call.",
                    steps,
                    toolResults);
            }

            steps.Add(new AgentExecutionStep(
                $"Model requested {toolCalls.Length} tool call(s) on iteration {iteration}."));

            foreach (var toolCall in toolCalls)
            {
                var toolCallSignature = CreateToolCallSignature(toolCall);
                if (!executedToolCalls.Add(toolCallSignature))
                {
                    sessionState = await HandleDuplicateToolCallAsync(
                        toolCall,
                        sessionState,
                        conversation,
                        steps,
                        cancellationToken);
                    continue;
                }

                var executionResult = await ExecuteToolCallAsync(tools, toolCall, steps, cancellationToken);
                toolResults.Add(executionResult);

                var output = BuildToolConversationOutput(
                    toolCall,
                    executionResult,
                    request.UserPrompt,
                    ref aggregatedEvidenceContext,
                    ref hasPendingWeakSearchEvidence,
                    ref weakSearchRecoveryGuidance);

                conversation.Add(new ModelToolResultMessage(toolCall.CallId, toolCall.ToolName, output));
                sessionState = await UpdateSessionStateAsync(
                    sessionState,
                    toolCall.ToolName,
                    output,
                    executionResult.Output,
                    cancellationToken);
            }
        }

        steps.Add(new AgentExecutionStep(
            $"Stopped after reaching the maximum of {_options.MaxIterations} model iterations.",
            false));

        return Failure(
            $"The agent stopped after reaching the maximum of {_options.MaxIterations} iterations.",
            steps,
            toolResults);
    }

    private async Task<ModelExecutionContext> InitializeModelExecutionAsync(
        AgentRequest request,
        ICollection<AgentExecutionStep> steps,
        CancellationToken cancellationToken)
    {
        steps.Add(new AgentExecutionStep($"Processing workspace: {request.WorkspacePath}"));

        var tools = _toolFactory(request.WorkspacePath);
        var registeredTools = GetRegisteredTools(tools);
        var sessionState = await LoadOrCreateSessionStateAsync(request, cancellationToken);

        steps.Add(new AgentExecutionStep(
            $"Registered tools: {string.Join(", ", registeredTools.Select(tool => tool.Name))}"));

        if (sessionState is not null)
        {
            steps.Add(new AgentExecutionStep(
                $"Loaded session '{sessionState.SessionId}' with {sessionState.VisitedFiles.Count} visited file(s)."));
        }

        return new ModelExecutionContext(tools, registeredTools, sessionState);
    }

    private bool TryCreateClarificationResponse(
        AgentRequest request,
        AgentSessionState? sessionState,
        ICollection<AgentExecutionStep> steps,
        IReadOnlyCollection<ToolExecutionResult> toolResults,
        out AgentResponse? clarificationResponse)
    {
        var clarification = _promptClarifier.GetClarification(request.UserPrompt, sessionState);
        if (clarification is null)
        {
            clarificationResponse = null;
            return false;
        }

        steps.Add(new AgentExecutionStep(
            "Prompt is ambiguous; asking for clarification before tool exploration."));
        clarificationResponse = new AgentResponse(clarification.Question, steps.ToArray(), toolResults);
        return true;
    }

    private static ModelRequest BuildModelRequest(
        IReadOnlyCollection<ModelConversationItem> conversation,
        IReadOnlyCollection<ToolDefinition> registeredTools,
        AgentSessionState? sessionState,
        string? previousResponseId)
    {
        return new ModelRequest(
            BuildModelInstructions(sessionState),
            conversation,
            registeredTools
                .Select(tool => new ModelToolDefinition(tool.Name, tool.Description, tool.Parameters))
                .ToArray(),
            previousResponseId);
    }

    private bool TryHandleFinalAnswer(
        string? finalAnswer,
        IReadOnlyCollection<ModelToolCall> toolCalls,
        bool hasPendingWeakSearchEvidence,
        string? weakSearchRecoveryGuidance,
        AggregatedEvidenceContext? aggregatedEvidenceContext,
        string userPrompt,
        int iteration,
        ICollection<AgentExecutionStep> steps,
        IReadOnlyCollection<ToolExecutionResult> toolResults,
        out AgentResponse? finalResponse,
        out string? followUpPrompt)
    {
        finalResponse = null;
        followUpPrompt = null;

        if (string.IsNullOrWhiteSpace(finalAnswer) || toolCalls.Count > 0)
        {
            return false;
        }

        if (hasPendingWeakSearchEvidence)
        {
            steps.Add(new AgentExecutionStep(
                "Model attempted to finalize after weak search evidence; requesting broader recovery instead.",
                false));
            followUpPrompt = BuildWeakSearchRecoveryPrompt(weakSearchRecoveryGuidance);
            return true;
        }

        if (RequiresMoreMultiFileEvidence(aggregatedEvidenceContext, userPrompt))
        {
            steps.Add(new AgentExecutionStep(
                "Model attempted to finalize before aggregating enough multi-file evidence; requesting one more supporting file.",
                false));
            followUpPrompt = BuildMultiFileAggregationPrompt(aggregatedEvidenceContext!);
            return true;
        }

        steps.Add(new AgentExecutionStep($"Model returned a final answer on iteration {iteration}."));
        finalResponse = new AgentResponse(finalAnswer, steps.ToArray(), toolResults);
        return true;
    }

    private async Task<AgentSessionState?> HandleDuplicateToolCallAsync(
        ModelToolCall toolCall,
        AgentSessionState? sessionState,
        ICollection<ModelConversationItem> conversation,
        ICollection<AgentExecutionStep> steps,
        CancellationToken cancellationToken)
    {
        var duplicateToolCallMessage = BuildDuplicateToolCallMessage(toolCall, sessionState);

        steps.Add(new AgentExecutionStep(
            $"Prevented duplicate tool call '{toolCall.ToolName}' with the same arguments.",
            false));

        conversation.Add(new ModelToolResultMessage(
            toolCall.CallId,
            toolCall.ToolName,
            duplicateToolCallMessage));

        return await UpdateSessionStateAsync(
            sessionState,
            toolCall.ToolName,
            duplicateToolCallMessage,
            null,
            cancellationToken);
    }

    private string BuildToolConversationOutput(
        ModelToolCall toolCall,
        ToolExecutionResult executionResult,
        string userPrompt,
        ref AggregatedEvidenceContext? aggregatedEvidenceContext,
        ref bool hasPendingWeakSearchEvidence,
        ref string? weakSearchRecoveryGuidance)
    {
        if (!executionResult.Success)
        {
            return executionResult.ErrorMessage ?? "Tool execution failed.";
        }

        UpdateWeakSearchState(
            toolCall.ToolName,
            executionResult.Output,
            userPrompt,
            ref hasPendingWeakSearchEvidence,
            ref weakSearchRecoveryGuidance);

        return CreateToolContextOutput(
            toolCall.ToolName,
            executionResult.Output,
            userPrompt,
            ref aggregatedEvidenceContext);
    }

    private async Task<AgentResponse> ProcessRuleBasedAsync(
        AgentRequest request,
        CancellationToken cancellationToken)
    {
        var steps = new List<AgentExecutionStep>();
        var toolResults = new List<ToolExecutionResult>();

        if (!TryValidateRequest(request, steps, toolResults, out var invalidResponse))
        {
            return invalidResponse!;
        }

        steps.Add(new AgentExecutionStep($"Processing workspace: {request.WorkspacePath}"));

        var tools = _toolFactory(request.WorkspacePath);
        var registeredTools = GetRegisteredTools(tools);

        steps.Add(new AgentExecutionStep(
            $"Registered tools: {string.Join(", ", registeredTools.Select(tool => tool.Name))}"));

        if (!TryGetTool(tools, ListFilesToolName, out var listFilesTool, out var listToolError))
        {
            steps.Add(new AgentExecutionStep(listToolError, false));
            return Failure(listToolError, steps, toolResults);
        }

        if (!TryGetTool(tools, ReadFileToolName, out var readFileTool, out var readToolError))
        {
            steps.Add(new AgentExecutionStep(readToolError, false));
            return Failure(readToolError, steps, toolResults);
        }

        var listFilesResult = await listFilesTool.ExecuteAsync(
            new Dictionary<string, string>
            {
                ["path"] = ".",
                ["recursive"] = "true",
                ["maxDepth"] = "6"
            },
            cancellationToken);

        toolResults.Add(listFilesResult);
        if (!listFilesResult.Success)
        {
            steps.Add(new AgentExecutionStep("Failed to list workspace files.", false));
            return Failure(
                listFilesResult.ErrorMessage ?? "Listing workspace files failed.",
                steps,
                toolResults);
        }

        steps.Add(new AgentExecutionStep("Listed workspace files."));

        var workspaceEntries = ParseWorkspaceEntries(listFilesResult.Output);
        var readmePath = workspaceEntries
            .Where(entry => !entry.IsDirectory && entry.Path.EndsWith("README.md", StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.Path.Equals("README.md", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .Select(entry => entry.Path)
            .FirstOrDefault();

        var projectFilePath = workspaceEntries
            .Where(entry => !entry.IsDirectory && entry.Path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .Select(entry => entry.Path)
            .FirstOrDefault();

        ReadFileObservation? readmeObservation = null;
        if (readmePath is not null)
        {
            readmeObservation = await ReadFileAsync(readFileTool, readmePath, toolResults, steps, cancellationToken);
        }
        else
        {
            steps.Add(new AgentExecutionStep("README.md was not found in the workspace."));
        }

        ReadFileObservation? projectObservation = null;
        if (projectFilePath is not null)
        {
            projectObservation = await ReadFileAsync(readFileTool, projectFilePath, toolResults, steps, cancellationToken);
        }
        else
        {
            steps.Add(new AgentExecutionStep("No .csproj file was found in the workspace."));
        }

        var summary = BuildFallbackSummary(request.UserPrompt, workspaceEntries, readmeObservation, projectObservation);
        return new AgentResponse(summary, steps, toolResults);
    }

    private async Task<AgentSessionState?> LoadOrCreateSessionStateAsync(
        AgentRequest request,
        CancellationToken cancellationToken)
    {
        if (_sessionStore is null)
        {
            return null;
        }

        var sessionId = GetSessionId(request);
        var existingState = await _sessionStore.GetAsync(sessionId, cancellationToken);
        if (existingState is not null)
        {
            return existingState;
        }

        var sessionState = new AgentSessionState
        {
            SessionId = sessionId,
            WorkspacePath = request.WorkspacePath
        };

        await _sessionStore.SaveAsync(sessionState, cancellationToken);
        return sessionState;
    }

    private async Task<AgentSessionState?> UpdateSessionStateAsync(
        AgentSessionState? sessionState,
        string toolName,
        string toolOutputForModel,
        string? rawToolOutput,
        CancellationToken cancellationToken)
    {
        if (sessionState is null || _sessionStore is null)
        {
            return sessionState;
        }

        var updatedVisitedFiles = UpdateRecentUniqueList(
            sessionState.VisitedFiles,
            ExtractVisitedFiles(toolName, rawToolOutput, _evidenceQualityEvaluator),
            20);

        if (_evidenceQualityEvaluator is not null)
        {
            updatedVisitedFiles = _evidenceQualityEvaluator.SelectPathsForSessionMemory(updatedVisitedFiles, 20);
        }

        var updatedHistory = sessionState.RecentToolHistory
            .Concat([CreateToolHistoryEntry(toolName, toolOutputForModel)])
            .TakeLast(8)
            .ToArray();

        var updatedState = sessionState with
        {
            VisitedFiles = updatedVisitedFiles,
            RecentToolHistory = updatedHistory
        };

        if (_sessionSummarizer is not null)
        {
            updatedState = updatedState with
            {
                WorkingSummary = _sessionSummarizer.UpdateSummary(updatedState, toolName, toolOutputForModel)
            };
        }

        await _sessionStore.SaveAsync(updatedState, cancellationToken);
        return updatedState;
    }

    private static IReadOnlyCollection<string> UpdateRecentUniqueList(
        IEnumerable<string> existingItems,
        IEnumerable<string> newItems,
        int maxEntries)
    {
        var recentItems = new List<string>();

        foreach (var item in existingItems.Concat(newItems))
        {
            if (string.IsNullOrWhiteSpace(item))
            {
                continue;
            }

            recentItems.RemoveAll(existing =>
                string.Equals(existing, item, StringComparison.OrdinalIgnoreCase));

            recentItems.Add(item);
        }

        if (recentItems.Count > maxEntries)
        {
            recentItems.RemoveRange(0, recentItems.Count - maxEntries);
        }

        return recentItems.ToArray();
    }

    private static bool TryValidateRequest(
        AgentRequest request,
        IReadOnlyCollection<AgentExecutionStep> steps,
        IReadOnlyCollection<ToolExecutionResult> toolResults,
        out AgentResponse? invalidResponse)
    {
        if (string.IsNullOrWhiteSpace(request.UserPrompt))
        {
            invalidResponse = Failure("A user prompt is required.", steps, toolResults);
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.WorkspacePath))
        {
            invalidResponse = Failure("A workspace path is required.", steps, toolResults);
            return false;
        }

        invalidResponse = null;
        return true;
    }

    private static AgentResponse Failure(
        string errorMessage,
        IReadOnlyCollection<AgentExecutionStep> steps,
        IReadOnlyCollection<ToolExecutionResult> toolResults)
    {
        return new AgentResponse(
            Output: string.Empty,
            ExecutionSteps: steps,
            ToolResults: toolResults,
            Success: false,
            ErrorMessage: errorMessage);
    }

    private static IReadOnlyCollection<ToolDefinition> GetRegisteredTools(IReadOnlyDictionary<string, ITool> tools)
    {
        return tools.Values
            .Select(tool => tool.Definition)
            .OrderBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string BuildModelInstructions(AgentSessionState? sessionState)
    {
        var builder = new StringBuilder(
            """
            You are ProjectLens, a repository analysis agent.
            You must answer using only:
            - the user's request
            - explicit tool outputs already present in the conversation

            Do not use outside knowledge or make up file contents, project details, or tool results.
            If you need more information, request a tool call using one of the provided tools.
            Use tool calls only when they materially help answer the user's request.
            Do not repeat the same tool call with the same arguments unless the earlier results were clearly insufficient.
            After search_files returns likely matches, prefer read_file on the most relevant result instead of repeating search_files.
            search_files may return hybrid candidates that combine exact keyword matches with bounded semantic chunk matches for conceptual queries; treat semantic hits as grounded candidate proposals, not as full-file proof.
            If multiple meaningful source files appear relevant to a logic, flow, architecture, or refactor question, inspect up to 2-3 of the top files and synthesize across them before finalizing.
            Distinguish the likely main flow file from supporting files when multiple files contribute to the answer.
            For feature-tracing prompts, prefer files closest to the requested feature intent such as feature-related controllers, services, entities/models, and frontend/API consumers.
            Do not default to Program.cs, startup wiring, or other setup/plumbing files as the main flow unless the evidence clearly shows the feature is implemented there.
            If the current feature context is marked provisional, do not treat any candidate file as settled truth yet.
            For follow-up prompts like "Which files appear to drive that feature?" or "Now suggest a refactor for that flow.", explicitly preserve that uncertainty, name the strongest current candidates, and avoid overconfident refactor guidance tied to unrelated flows.
            If an exact keyword search returns only low-value, generated, config, project, or other non-source matches, treat that as weak evidence rather than enough support for a final logic answer.
            When search evidence is weak, prefer one bounded recovery step: either broaden the search with related implementation terms or inspect a likely main source file before answering.
            For follow-up requests about refactoring, improving, or explaining logic you already inspected, use the existing session context and prior file summaries first.
            If the session already identifies a likely file or flow, propose the best grounded answer or refactor direction you can before requesting more tool calls.
            For refactor, design, or code-improvement prompts, clearly separate observed facts from inferred recommendations.
            Present observed facts only from actual tool output.
            Present refactor ideas, extraction plans, candidate classes, candidate methods, and skeleton structures as inferred recommendations unless they were explicitly observed.
            If the evidence is partial, preview-based, snippet-based, or truncated, say that clearly and keep recommendations at a skeleton level.
            Avoid unnecessary or redundant tool calls.
            Return a final answer as soon as enough evidence is available.
            If the evidence is partial, answer with uncertainty rather than looping forever.
            When you give a final answer, ground every claim in either the user's request or prior tool outputs.
            If the available evidence is insufficient, say that directly.
            """);

        if (sessionState is null)
        {
            return builder.ToString();
        }

        if (!string.IsNullOrWhiteSpace(sessionState.WorkingSummary) ||
            sessionState.VisitedFiles.Count > 0 ||
            sessionState.RecentToolHistory.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Existing session context for this workspace:");

            if (IsProvisionalFeatureContext(sessionState.WorkingSummary))
            {
                builder.AppendLine("The existing feature-flow context is provisional; treat candidate files as hypotheses until enough supporting evidence is read.");
            }

            if (!string.IsNullOrWhiteSpace(sessionState.WorkingSummary))
            {
                builder.AppendLine("Working summary:");
                builder.AppendLine(sessionState.WorkingSummary);
            }

            if (sessionState.VisitedFiles.Count > 0)
            {
                builder.AppendLine($"Visited files: {string.Join(", ", sessionState.VisitedFiles.Take(10))}");
            }

            if (sessionState.RecentToolHistory.Count > 0)
            {
                builder.AppendLine("Recent tool history:");
                foreach (var historyEntry in sessionState.RecentToolHistory.TakeLast(5))
                {
                    builder.AppendLine($"- {historyEntry}");
                }
            }
        }

        return builder.ToString();
    }

    private string CreateToolContextOutput(
        string toolName,
        string? rawToolOutput,
        string userPrompt,
        ref AggregatedEvidenceContext? aggregatedEvidenceContext)
    {
        if (string.IsNullOrWhiteSpace(rawToolOutput))
        {
            return string.Empty;
        }

        if (string.Equals(toolName, ReadFileToolName, StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseReadFilePayload(rawToolOutput, out var path, out var content, out var isTruncated, out var characterCount))
            {
                return rawToolOutput;
            }

            var compressedOutput = _fileCompressor?.Compress(path, content, userPrompt) ?? rawToolOutput;
            var evidenceBasis = isTruncated
                ? $"Evidence basis: read_file returned truncated content at {characterCount} characters; recommendations should stay grounded to that partial excerpt."
                : $"Evidence basis: read_file returned a bounded excerpt of {characterCount} characters; use observed facts from this excerpt and label broader refactor ideas as inferred.";
            var baseOutput = $"{compressedOutput}{Environment.NewLine}{evidenceBasis}";
            aggregatedEvidenceContext = UpdateAggregatedEvidenceFromRead(
                aggregatedEvidenceContext,
                path,
                baseOutput);
            return AppendAggregatedEvidenceContext(baseOutput, aggregatedEvidenceContext);
        }

        if (string.Equals(toolName, SearchFilesToolName, StringComparison.OrdinalIgnoreCase))
        {
            aggregatedEvidenceContext = BuildAggregatedEvidenceContext(
                rawToolOutput,
                userPrompt,
                _evidenceQualityEvaluator,
                aggregatedEvidenceContext);
            var searchSummary = SummarizeSearchResults(rawToolOutput, userPrompt, _evidenceQualityEvaluator);
            return AppendAggregatedEvidenceContext(searchSummary, aggregatedEvidenceContext);
        }

        return rawToolOutput;
    }

    private static string SummarizeSearchResults(
        string rawToolOutput,
        string userPrompt,
        IEvidenceQualityEvaluator? evidenceQualityEvaluator)
    {
        try
        {
            using var document = JsonDocument.Parse(rawToolOutput);
            var root = document.RootElement;
            var query = root.TryGetProperty("Query", out var queryElement) ? queryElement.GetString() : null;
            var totalMatches = root.TryGetProperty("TotalMatches", out var totalMatchesElement)
                ? totalMatchesElement.GetInt32()
                : 0;
            var retrievalMode = root.TryGetProperty("RetrievalMode", out var retrievalModeElement)
                ? retrievalModeElement.GetString() ?? "keyword"
                : "keyword";
            var keywordMatchCount = root.TryGetProperty("KeywordMatchCount", out var keywordCountElement)
                ? keywordCountElement.GetInt32()
                : totalMatches;
            var semanticMatchCount = root.TryGetProperty("SemanticMatchCount", out var semanticCountElement)
                ? semanticCountElement.GetInt32()
                : 0;
            var matches = root.TryGetProperty("Matches", out var matchesElement)
                ? matchesElement.EnumerateArray()
                    .Select(match => new EvidenceMatch(
                        match.TryGetProperty("Path", out var pathElement) ? pathElement.GetString() ?? string.Empty : string.Empty,
                        match.TryGetProperty("Snippet", out var snippetElement) ? snippetElement.GetString() ?? string.Empty : string.Empty,
                        match.TryGetProperty("LineNumber", out var lineNumberElement) ? lineNumberElement.GetInt32() : 0,
                        match.TryGetProperty("MatchKind", out var matchKindElement) ? matchKindElement.GetString() ?? "keyword" : "keyword",
                        match.TryGetProperty("SimilarityScore", out var similarityElement) ? similarityElement.GetDouble() : 0,
                        match.TryGetProperty("EndLineNumber", out var endLineNumberElement) ? endLineNumberElement.GetInt32() : 0))
                    .Where(match => !string.IsNullOrWhiteSpace(match.Path))
                    .ToArray()
                : Array.Empty<EvidenceMatch>();
            var searchEvidence = evidenceQualityEvaluator is null
                ? new SearchEvidenceAssessment(matches.Take(5).ToArray(), false, matches.Any(), string.Empty)
                : evidenceQualityEvaluator.AssessSearchEvidence(matches, query ?? userPrompt, 5);

            var builder = new StringBuilder();
            builder.AppendLine($"search_files query: {query}");
            builder.AppendLine($"Total matches: {totalMatches}");
            builder.AppendLine($"Retrieval mode: {retrievalMode} (keyword candidates: {keywordMatchCount}, semantic candidates: {semanticMatchCount})");
            builder.AppendLine("Evidence basis: search_files returns bounded filename/snippet candidates only; semantic matches come from chunk-level similarity and still require read_file before making file-level claims.");

            if (searchEvidence.IsWeakEvidence)
            {
                builder.AppendLine(searchEvidence.RecoveryGuidance);
            }

            foreach (var match in searchEvidence.RankedMatches)
            {
                var semanticSuffix = match.MatchKind.Equals("semantic", StringComparison.OrdinalIgnoreCase)
                    ? $" [semantic {match.SimilarityScore:F2}]"
                    : string.Empty;
                builder.AppendLine($"- {match.Path}{semanticSuffix}: {match.Snippet}");
            }

            return builder.ToString().TrimEnd();
        }
        catch
        {
            return rawToolOutput;
        }
    }

    private static bool TryParseReadFilePayload(
        string rawToolOutput,
        out string path,
        out string content,
        out bool isTruncated,
        out int characterCount)
    {
        path = string.Empty;
        content = string.Empty;
        isTruncated = false;
        characterCount = 0;

        try
        {
            using var document = JsonDocument.Parse(rawToolOutput);
            var root = document.RootElement;
            path = root.GetProperty("Path").GetString() ?? string.Empty;
            content = root.GetProperty("Content").GetString() ?? string.Empty;
            isTruncated = root.TryGetProperty("IsTruncated", out var isTruncatedElement) && isTruncatedElement.GetBoolean();
            characterCount = root.TryGetProperty("CharacterCount", out var characterCountElement)
                ? characterCountElement.GetInt32()
                : content.Length;
            return !string.IsNullOrWhiteSpace(path);
        }
        catch
        {
            return false;
        }
    }

    private static IReadOnlyCollection<string> ExtractVisitedFiles(
        string toolName,
        string? rawToolOutput,
        IEvidenceQualityEvaluator? evidenceQualityEvaluator)
    {
        if (string.IsNullOrWhiteSpace(rawToolOutput))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(rawToolOutput);
            var root = document.RootElement;

            if (string.Equals(toolName, ReadFileToolName, StringComparison.OrdinalIgnoreCase))
            {
                var path = root.TryGetProperty("Path", out var pathElement)
                    ? pathElement.GetString()
                    : null;

                return string.IsNullOrWhiteSpace(path)
                    ? Array.Empty<string>()
                    : evidenceQualityEvaluator is null
                        ? [path]
                        : evidenceQualityEvaluator.SelectPathsForSessionMemory([path], 1);
            }

            if (string.Equals(toolName, SearchFilesToolName, StringComparison.OrdinalIgnoreCase) &&
                root.TryGetProperty("Matches", out var matchesElement))
            {
                var matches = matchesElement
                    .EnumerateArray()
                    .Select(match => new EvidenceMatch(
                        match.TryGetProperty("Path", out var pathElement) ? pathElement.GetString() ?? string.Empty : string.Empty,
                        match.TryGetProperty("Snippet", out var snippetElement) ? snippetElement.GetString() ?? string.Empty : string.Empty,
                        match.TryGetProperty("LineNumber", out var lineNumberElement) ? lineNumberElement.GetInt32() : 0))
                    .Where(match => !string.IsNullOrWhiteSpace(match.Path))
                    .ToArray();

                if (evidenceQualityEvaluator is null)
                {
                    return matches.Take(5).Select(match => match.Path).ToArray();
                }

                var query = root.TryGetProperty("Query", out var queryElement) ? queryElement.GetString() : null;
                var rankedPaths = evidenceQualityEvaluator
                    .RankMatches(matches, query, 5)
                    .Select(match => match.Path);

                return evidenceQualityEvaluator.SelectPathsForSessionMemory(rankedPaths, 5);
            }
        }
        catch
        {
            return Array.Empty<string>();
        }

        return Array.Empty<string>();
    }

    private static string CreateToolHistoryEntry(string toolName, string toolOutput)
    {
        var normalizedOutput = string.Join(
            ' ',
            toolOutput.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        const int maxLength = 180;
        if (normalizedOutput.Length > maxLength)
        {
            normalizedOutput = normalizedOutput[..(maxLength - 3)].TrimEnd() + "...";
        }

        return $"{toolName}: {normalizedOutput}";
    }

    private static string BuildDuplicateToolCallMessage(ModelToolCall toolCall, AgentSessionState? sessionState)
    {
        var builder = new StringBuilder();
        builder.Append("This exact tool call was already executed earlier, so rerunning it is unlikely to add new evidence. ");
        builder.Append("Reuse the prior tool output already in the conversation. ");
        builder.Append("Choose a different action if you still need more evidence. ");
        builder.Append("If you answer now from partial evidence, separate observed facts from inferred recommendations. ");

        if (string.Equals(toolCall.ToolName, SearchFilesToolName, StringComparison.OrdinalIgnoreCase))
        {
            builder.Append("Prefer read_file on the most relevant matched or recently visited file, or provide the best grounded answer now if the current evidence is sufficient. ");
        }
        else if (string.Equals(toolCall.ToolName, ReadFileToolName, StringComparison.OrdinalIgnoreCase))
        {
            builder.Append("Use the prior compressed file summary for reasoning, inspect a different relevant file, or provide the best grounded answer with any uncertainty called out. ");
        }
        else
        {
            builder.Append("Choose a different action or provide the best grounded answer possible. ");
        }

        if (sessionState is not null)
        {
            if (sessionState.VisitedFiles.Count > 0)
            {
                builder.Append($"Recently visited files: {string.Join(", ", sessionState.VisitedFiles.TakeLast(5))}. ");
            }

            if (!string.IsNullOrWhiteSpace(sessionState.WorkingSummary))
            {
                builder.Append($"Working summary: {CreateSnippet(sessionState.WorkingSummary)}");
            }
        }

        return builder.ToString().Trim();
    }

    private static string GetSessionId(AgentRequest request)
    {
        if (request.Context is not null &&
            request.Context.TryGetValue(SessionIdContextKey, out var sessionId) &&
            !string.IsNullOrWhiteSpace(sessionId))
        {
            return sessionId.Trim();
        }

        return Path.GetFullPath(request.WorkspacePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToLowerInvariant();
    }

    private static string CreateToolCallSignature(ModelToolCall toolCall)
    {
        var normalizedArguments = toolCall.Arguments
            .OrderBy(argument => argument.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                argument => argument.Key,
                argument => argument.Value?.Trim() ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);

        return $"{toolCall.ToolName.Trim().ToLowerInvariant()}:{JsonSerializer.Serialize(normalizedArguments)}";
    }

    private void UpdateWeakSearchState(
        string toolName,
        string? rawToolOutput,
        string userPrompt,
        ref bool hasPendingWeakSearchEvidence,
        ref string? weakSearchRecoveryGuidance)
    {
        if (string.Equals(toolName, SearchFilesToolName, StringComparison.OrdinalIgnoreCase))
        {
            var searchEvidence = AnalyzeSearchEvidence(rawToolOutput, userPrompt, _evidenceQualityEvaluator);
            hasPendingWeakSearchEvidence = searchEvidence?.IsWeakEvidence == true;
            weakSearchRecoveryGuidance = searchEvidence?.RecoveryGuidance;
            return;
        }

        if (string.Equals(toolName, ReadFileToolName, StringComparison.OrdinalIgnoreCase) &&
            _evidenceQualityEvaluator is not null &&
            TryParseReadFilePayload(rawToolOutput ?? string.Empty, out var path, out _, out _, out _) &&
            _evidenceQualityEvaluator.IsMeaningfulSourcePath(path))
        {
            hasPendingWeakSearchEvidence = false;
            weakSearchRecoveryGuidance = null;
        }
    }

    private static SearchEvidenceAssessment? AnalyzeSearchEvidence(
        string? rawToolOutput,
        string userPrompt,
        IEvidenceQualityEvaluator? evidenceQualityEvaluator)
    {
        if (string.IsNullOrWhiteSpace(rawToolOutput))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(rawToolOutput);
            var root = document.RootElement;
            var matches = root.TryGetProperty("Matches", out var matchesElement)
                ? matchesElement.EnumerateArray()
                    .Select(match => new EvidenceMatch(
                        match.TryGetProperty("Path", out var pathElement) ? pathElement.GetString() ?? string.Empty : string.Empty,
                        match.TryGetProperty("Snippet", out var snippetElement) ? snippetElement.GetString() ?? string.Empty : string.Empty,
                        match.TryGetProperty("LineNumber", out var lineNumberElement) ? lineNumberElement.GetInt32() : 0))
                    .Where(match => !string.IsNullOrWhiteSpace(match.Path))
                    .ToArray()
                : Array.Empty<EvidenceMatch>();
            var query = root.TryGetProperty("Query", out var queryElement) ? queryElement.GetString() : null;

            return evidenceQualityEvaluator is null
                ? new SearchEvidenceAssessment(matches.Take(5).ToArray(), false, matches.Any(), string.Empty)
                : evidenceQualityEvaluator.AssessSearchEvidence(matches, query ?? userPrompt, 5);
        }
        catch
        {
            return null;
        }
    }

    private static string BuildWeakSearchRecoveryPrompt(string? recoveryGuidance = null)
    {
        var guidance = string.IsNullOrWhiteSpace(recoveryGuidance)
            ? "Prefer one bounded recovery step: broaden the search with related implementation terms or inspect a likely main source file before answering."
            : recoveryGuidance.Trim();

        return $"The previous exact keyword search was weak evidence for a logic explanation. Do not finalize yet. {guidance}";
    }

    private static bool RequiresMoreMultiFileEvidence(
        AggregatedEvidenceContext? aggregatedEvidenceContext,
        string userPrompt)
    {
        if (aggregatedEvidenceContext is null || !RequiresMultiFileSynthesis(userPrompt))
        {
            return false;
        }

        var selectedFileCount = aggregatedEvidenceContext.Files.Count;
        if (selectedFileCount < 2)
        {
            return false;
        }

        var observedFileCount = aggregatedEvidenceContext.Files.Count(file =>
            !string.IsNullOrWhiteSpace(file.ObservationSummary));

        var requiredObservedFiles = Math.Min(2, selectedFileCount);

        return observedFileCount < requiredObservedFiles;
    }

    private static bool RequiresMultiFileSynthesis(string userPrompt)
    {
        if (string.IsNullOrWhiteSpace(userPrompt))
        {
            return false;
        }

        var normalizedPrompt = userPrompt.ToLowerInvariant();
        var synthesisSignals = new[]
        {
            "flow", "logic", "architecture", "explain", "how", "refactor", "structure", "interaction", "pipeline"
        };

        return synthesisSignals.Any(signal => normalizedPrompt.Contains(signal, StringComparison.Ordinal));
    }

    private AggregatedEvidenceContext? BuildAggregatedEvidenceContext(
        string rawToolOutput,
        string userPrompt,
        IEvidenceQualityEvaluator? evidenceQualityEvaluator,
        AggregatedEvidenceContext? existingContext)
    {
        if (evidenceQualityEvaluator is null || !RequiresMultiFileSynthesis(userPrompt))
        {
            return existingContext;
        }

        try
        {
            using var document = JsonDocument.Parse(rawToolOutput);
            var root = document.RootElement;
            if (!root.TryGetProperty("Matches", out var matchesElement))
            {
                return existingContext;
            }

            var query = root.TryGetProperty("Query", out var queryElement) ? queryElement.GetString() : null;
            var allMatches = matchesElement.EnumerateArray()
                .Select(match => new EvidenceMatch(
                    match.TryGetProperty("Path", out var pathElement) ? pathElement.GetString() ?? string.Empty : string.Empty,
                    match.TryGetProperty("Snippet", out var snippetElement) ? snippetElement.GetString() ?? string.Empty : string.Empty,
                    match.TryGetProperty("LineNumber", out var lineNumberElement) ? lineNumberElement.GetInt32() : 0))
                .Where(match => !string.IsNullOrWhiteSpace(match.Path))
                .ToArray();
            var rankedMatches = evidenceQualityEvaluator
                .RankMatches(allMatches, query ?? userPrompt, Math.Min(20, Math.Max(allMatches.Length, 5)));
            var isFeatureTracingPrompt = evidenceQualityEvaluator.IsFeatureTracingPrompt(userPrompt);

            var selectedPaths = rankedMatches
                .Where(match => evidenceQualityEvaluator.IsMeaningfulSourcePath(match.Path))
                .GroupBy(match => match.Path, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderByDescending(match => GetAggregationPriority(match.Path, match.Snippet, userPrompt, evidenceQualityEvaluator))
                .ThenByDescending(match => evidenceQualityEvaluator.ScoreFile(match.Path, match.Snippet, query ?? userPrompt))
                .ThenBy(match => match.Path, StringComparer.OrdinalIgnoreCase)
                .Select(match => match.Path)
                .Take(3)
                .ToArray();

            if (selectedPaths.Length < 2)
            {
                return existingContext;
            }

            var selectedFiles = selectedPaths
                .Select((path, index) => new AggregatedEvidenceFile(
                    path,
                    index == 0
                        ? "Likely main flow file based on the strongest meaningful source match for the current request."
                        : "Supporting source file selected as additional relevant evidence for the current request.",
                    existingContext?.Files.FirstOrDefault(file =>
                        string.Equals(file.Path, path, StringComparison.OrdinalIgnoreCase))?.ObservationSummary ?? string.Empty))
                .ToArray();

            var evidenceLimitations = new List<string>
            {
                $"Selection is based on ranked search matches and snippets; {CountObservedFiles(selectedFiles)} of {selectedFiles.Length} selected file(s) have been read so far."
            };

            if (isFeatureTracingPrompt && CountObservedFiles(selectedFiles) < Math.Min(2, selectedFiles.Length))
            {
                evidenceLimitations.Add("Feature flow is still being traced; the current main-flow file is provisional until more supporting files are read.");
            }

            return new AggregatedEvidenceContext(
                isFeatureTracingPrompt,
                isFeatureTracingPrompt,
                selectedFiles[0].Path,
                selectedFiles,
                evidenceLimitations);
        }
        catch
        {
            return existingContext;
        }
    }

    private static AggregatedEvidenceContext? UpdateAggregatedEvidenceFromRead(
        AggregatedEvidenceContext? aggregatedEvidenceContext,
        string path,
        string baseOutput)
    {
        if (aggregatedEvidenceContext is null)
        {
            return null;
        }

        var updatedFiles = aggregatedEvidenceContext.Files
            .Select(file => string.Equals(file.Path, path, StringComparison.OrdinalIgnoreCase)
                ? file with { ObservationSummary = CreateSnippet(baseOutput) }
                : file)
            .ToArray();

        if (updatedFiles.All(file => string.IsNullOrWhiteSpace(file.ObservationSummary)))
        {
            return aggregatedEvidenceContext;
        }

        var evidenceLimitations = new[]
        {
            $"Multi-file aggregation currently covers {CountObservedFiles(updatedFiles)} of {updatedFiles.Length} selected file(s)."
        }.Concat(aggregatedEvidenceContext.EvidenceLimitations
            .Where(limitation => limitation.Contains("provisional", StringComparison.OrdinalIgnoreCase)))
        .Distinct(StringComparer.Ordinal)
        .ToArray();

        var isProvisional = aggregatedEvidenceContext.IsFeatureTrace && CountObservedFiles(updatedFiles) < 2;
        if (!isProvisional)
        {
            evidenceLimitations = evidenceLimitations
                .Where(limitation => !limitation.Contains("provisional", StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        return aggregatedEvidenceContext with
        {
            IsProvisional = isProvisional,
            Files = updatedFiles,
            EvidenceLimitations = evidenceLimitations
        };
    }

    private static string AppendAggregatedEvidenceContext(
        string output,
        AggregatedEvidenceContext? aggregatedEvidenceContext)
    {
        if (aggregatedEvidenceContext is null)
        {
            return output;
        }

        var builder = new StringBuilder(output.TrimEnd());
        builder.AppendLine();
        builder.AppendLine("Aggregation context:");

        if (aggregatedEvidenceContext.IsFeatureTrace)
        {
            builder.AppendLine($"Feature flow confidence: {(aggregatedEvidenceContext.IsProvisional ? "provisional" : "strong")}");
        }

        if (!string.IsNullOrWhiteSpace(aggregatedEvidenceContext.LikelyMainFlowFile))
        {
            builder.AppendLine($"Likely main flow file: {aggregatedEvidenceContext.LikelyMainFlowFile}");
        }

        foreach (var file in aggregatedEvidenceContext.Files)
        {
            if (string.Equals(file.Path, aggregatedEvidenceContext.LikelyMainFlowFile, StringComparison.OrdinalIgnoreCase))
            {
                builder.AppendLine($"Selected file: {file.Path} | reason: {file.SelectionReason}");
            }
            else
            {
                builder.AppendLine($"Supporting file: {file.Path} | reason: {file.SelectionReason}");
            }

            if (!string.IsNullOrWhiteSpace(file.ObservationSummary))
            {
                builder.AppendLine($"Observed file summary: {file.Path} => {file.ObservationSummary}");
            }
        }

        foreach (var limitation in aggregatedEvidenceContext.EvidenceLimitations)
        {
            builder.AppendLine($"Aggregation limitation: {limitation}");
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildMultiFileAggregationPrompt(AggregatedEvidenceContext aggregatedEvidenceContext)
    {
        var unreadSupportingFiles = aggregatedEvidenceContext.Files
            .Where(file =>
                !string.Equals(file.Path, aggregatedEvidenceContext.LikelyMainFlowFile, StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(file.ObservationSummary))
            .Select(file => file.Path)
            .Take(2)
            .ToArray();

        var targetFiles = unreadSupportingFiles.Length > 0
            ? string.Join(", ", unreadSupportingFiles)
            : string.Join(", ", aggregatedEvidenceContext.Files
                .Where(file => string.IsNullOrWhiteSpace(file.ObservationSummary))
                .Select(file => file.Path)
                .Take(2));

        return $"Multiple meaningful source files appear relevant. Do not finalize yet. Inspect one more supporting file before answering, preferably from: {targetFiles}. For feature tracing, prioritize controller/service/model/frontend files that are closest to the requested feature intent. Then synthesize across the likely main flow file and supporting files, separating observed facts from inferred recommendations.";
    }

    private static int CountObservedFiles(IEnumerable<AggregatedEvidenceFile> files)
    {
        return files.Count(file => !string.IsNullOrWhiteSpace(file.ObservationSummary));
    }

    private static bool IsProvisionalFeatureContext(string? workingSummary)
    {
        return !string.IsNullOrWhiteSpace(workingSummary) &&
            (workingSummary.Contains("Feature flow confidence: provisional", StringComparison.OrdinalIgnoreCase) ||
             workingSummary.Contains("Current feature-flow understanding is provisional", StringComparison.OrdinalIgnoreCase));
    }

    private static int GetAggregationPriority(
        string path,
        string snippet,
        string userPrompt,
        IEvidenceQualityEvaluator evidenceQualityEvaluator)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(userPrompt))
        {
            return 0;
        }

        var lowerPath = path.ToLowerInvariant();
        var lowerPrompt = userPrompt.ToLowerInvariant();
        var score = 0;
        var isFeatureTracingPrompt = evidenceQualityEvaluator.IsFeatureTracingPrompt(userPrompt);
        var intentTerms = evidenceQualityEvaluator.ExpandIntentTerms(userPrompt);
        var matchedIntentTerms = intentTerms.Count(term =>
            lowerPath.Contains(term, StringComparison.Ordinal) ||
            snippet.Contains(term, StringComparison.OrdinalIgnoreCase));

        if (lowerPrompt.Contains("flow", StringComparison.Ordinal) ||
            lowerPrompt.Contains("architecture", StringComparison.Ordinal) ||
            lowerPrompt.Contains("explain", StringComparison.Ordinal) ||
            lowerPrompt.Contains("how", StringComparison.Ordinal))
        {
            if (lowerPath.Contains("controller", StringComparison.Ordinal) ||
                lowerPath.Contains("program", StringComparison.Ordinal) ||
                lowerPath.Contains("startup", StringComparison.Ordinal) ||
                lowerPath.Contains("handler", StringComparison.Ordinal) ||
                lowerPath.Contains("service", StringComparison.Ordinal) ||
                lowerPath.Contains("manager", StringComparison.Ordinal) ||
                lowerPath.Contains("orchestrator", StringComparison.Ordinal))
            {
                score += 40;
            }

            if (lowerPath.Contains("repository", StringComparison.Ordinal))
            {
                score += 5;
            }

            if (lowerPath.Contains("helper", StringComparison.Ordinal) ||
                lowerPath.Contains("util", StringComparison.Ordinal) ||
                lowerPath.Contains("utility", StringComparison.Ordinal))
            {
                score -= 25;
            }
        }

        if (isFeatureTracingPrompt)
        {
            score += matchedIntentTerms * 10;

            if (lowerPath.Contains("controller", StringComparison.Ordinal))
            {
                score += 30;
            }

            if (lowerPath.Contains("service", StringComparison.Ordinal))
            {
                score += 26;
            }

            if (lowerPath.Contains("entity", StringComparison.Ordinal) ||
                lowerPath.Contains("model", StringComparison.Ordinal))
            {
                score += 18;
            }

            if (lowerPath.EndsWith("app.jsx", StringComparison.Ordinal) ||
                lowerPath.EndsWith("app.tsx", StringComparison.Ordinal) ||
                lowerPath.EndsWith("app.js", StringComparison.Ordinal) ||
                lowerPath.EndsWith("app.ts", StringComparison.Ordinal) ||
                lowerPath.Contains("/src/", StringComparison.Ordinal))
            {
                score += 16;
            }

            if (lowerPath.EndsWith("program.cs", StringComparison.Ordinal) ||
                lowerPath.EndsWith("startup.cs", StringComparison.Ordinal) ||
                lowerPath.Contains("launchsettings", StringComparison.Ordinal) ||
                lowerPath.Contains("/config/", StringComparison.Ordinal) ||
                lowerPath.Contains("/configuration/", StringComparison.Ordinal))
            {
                score -= 45;
            }
        }

        return score;
    }

    private async Task<ToolExecutionResult> ExecuteToolCallAsync(
        IReadOnlyDictionary<string, ITool> tools,
        ModelToolCall toolCall,
        ICollection<AgentExecutionStep> steps,
        CancellationToken cancellationToken)
    {
        if (!TryGetTool(tools, toolCall.ToolName, out var tool, out var errorMessage))
        {
            steps.Add(new AgentExecutionStep(errorMessage, false));
            return new ToolExecutionResult(toolCall.ToolName, false, null, errorMessage);
        }

        steps.Add(new AgentExecutionStep($"Executing tool '{toolCall.ToolName}' (call id: {toolCall.CallId})."));
        var result = await tool.ExecuteAsync(toolCall.Arguments, cancellationToken);

        steps.Add(new AgentExecutionStep(
            result.Success
                ? $"Tool '{toolCall.ToolName}' completed successfully."
                : $"Tool '{toolCall.ToolName}' failed: {result.ErrorMessage}",
            result.Success));

        return result;
    }

    private static string BuildFallbackSummary(
        string userPrompt,
        IReadOnlyCollection<WorkspaceEntry> workspaceEntries,
        ReadFileObservation? readmeObservation,
        ReadFileObservation? projectObservation)
    {
        var directoryCount = workspaceEntries.Count(entry => entry.IsDirectory);
        var fileCount = workspaceEntries.Count - directoryCount;
        var builder = new StringBuilder();

        builder.AppendLine($"User prompt: {userPrompt}");
        builder.AppendLine();
        builder.AppendLine("Grounded workspace summary:");
        builder.AppendLine($"- The workspace inspection found {fileCount} files and {directoryCount} directories.");

        if (readmeObservation is not null)
        {
            builder.AppendLine(
                $"- README.md ({readmeObservation.Path}) says: {CreateSnippet(readmeObservation.Content)}");
        }
        else
        {
            builder.AppendLine("- README.md was not available, so the summary does not include repository notes.");
        }

        if (projectObservation is not null)
        {
            builder.AppendLine($"- Project file: {DescribeProjectFile(projectObservation)}");
        }
        else
        {
            builder.AppendLine("- No .csproj file was available, so project metadata could not be inspected.");
        }

        builder.Append("- This summary is based only on the files listed and read by the registered tools.");
        return builder.ToString();
    }

    private static string CreateSnippet(string content)
    {
        var normalized = string.Join(
            ' ',
            content.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        const int maxLength = 220;
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return normalized[..maxLength].TrimEnd() + "...";
    }

    private static string DescribeProjectFile(ReadFileObservation observation)
    {
        try
        {
            var document = XDocument.Parse(observation.Content);
            var root = document.Root;
            if (root is null)
            {
                return $"{observation.Path} was read, but it did not contain a valid XML root element.";
            }

            var targetFramework = root
                .Descendants()
                .FirstOrDefault(element => element.Name.LocalName is "TargetFramework" or "TargetFrameworks")
                ?.Value
                ?.Trim();

            var outputType = root
                .Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "OutputType")
                ?.Value
                ?.Trim();

            var summaryParts = new List<string> { observation.Path };

            if (!string.IsNullOrWhiteSpace(targetFramework))
            {
                summaryParts.Add($"targets {targetFramework}");
            }

            if (!string.IsNullOrWhiteSpace(outputType))
            {
                summaryParts.Add($"outputs {outputType}");
            }

            return string.Join(", ", summaryParts);
        }
        catch
        {
            return $"{observation.Path} was read, but its XML metadata could not be parsed.";
        }
    }

    private static IReadOnlyCollection<WorkspaceEntry> ParseWorkspaceEntries(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return Array.Empty<WorkspaceEntry>();
        }

        using var document = JsonDocument.Parse(output);
        if (!document.RootElement.TryGetProperty("Entries", out var entriesElement))
        {
            return Array.Empty<WorkspaceEntry>();
        }

        var entries = new List<WorkspaceEntry>();
        foreach (var entryElement in entriesElement.EnumerateArray())
        {
            var path = entryElement.GetProperty("Path").GetString();
            var isDirectory = entryElement.GetProperty("IsDirectory").GetBoolean();

            if (!string.IsNullOrWhiteSpace(path))
            {
                entries.Add(new WorkspaceEntry(path, isDirectory));
            }
        }

        return entries;
    }

    private static ReadFileObservation ParseReadFileObservation(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            throw new InvalidOperationException("Expected a file read payload.");
        }

        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;

        return new ReadFileObservation(
            root.GetProperty("Path").GetString() ?? string.Empty,
            root.GetProperty("Content").GetString() ?? string.Empty,
            root.GetProperty("IsTruncated").GetBoolean(),
            root.GetProperty("CharacterCount").GetInt32());
    }

    private async Task<ReadFileObservation?> ReadFileAsync(
        ITool readFileTool,
        string path,
        ICollection<ToolExecutionResult> toolResults,
        ICollection<AgentExecutionStep> steps,
        CancellationToken cancellationToken)
    {
        var result = await readFileTool.ExecuteAsync(
            new Dictionary<string, string> { ["path"] = path },
            cancellationToken);

        toolResults.Add(result);
        if (!result.Success)
        {
            steps.Add(new AgentExecutionStep($"Failed to read {path}.", false));
            return null;
        }

        var observation = ParseReadFileObservation(result.Output);
        var truncationSuffix = observation.IsTruncated ? " (truncated)" : string.Empty;
        steps.Add(new AgentExecutionStep($"Read {observation.Path}{truncationSuffix}."));
        return observation;
    }

    private static bool TryGetTool(
        IReadOnlyDictionary<string, ITool> tools,
        string toolName,
        out ITool tool,
        out string errorMessage)
    {
        if (tools.TryGetValue(toolName, out tool!))
        {
            errorMessage = string.Empty;
            return true;
        }

        errorMessage = $"The required tool '{toolName}' is not registered.";
        return false;
    }

    private sealed record WorkspaceEntry(string Path, bool IsDirectory);

    private sealed record ReadFileObservation(
        string Path,
        string Content,
        bool IsTruncated,
        int CharacterCount);

    private sealed record ModelExecutionContext(
        IReadOnlyDictionary<string, ITool> Tools,
        IReadOnlyCollection<ToolDefinition> RegisteredTools,
        AgentSessionState? SessionState);
}
