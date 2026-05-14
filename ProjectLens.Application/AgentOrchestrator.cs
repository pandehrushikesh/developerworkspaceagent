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
    private const int MaxSearchFilesCallsPerIteration = 1;
    private const int MaxReadFileCallsPerIteration = 2;

    private readonly IInstructionBuilder _instructionBuilder;
    private readonly IEvidenceEvaluator _evidenceEvaluator;
    private readonly IConvergencePolicy _convergencePolicy;
    private readonly IModelClient? _modelClient;
    private readonly AgentOrchestratorOptions _options;
    private readonly IPromptClarifier _promptClarifier;
    private readonly IRecoveryPolicy _recoveryPolicy;
    private readonly ISessionContextService _sessionContextService;
    private readonly IToolOutputAdapter _toolOutputAdapter;
    private readonly Func<string, IReadOnlyDictionary<string, ITool>> _toolFactory;

    public AgentOrchestrator(
        IEnumerable<ITool> tools,
        AgentOrchestrationDependencies dependencies,
        IModelClient? modelClient = null,
        AgentOrchestratorOptions? options = null)
        : this(
            _ => tools,
            dependencies,
            modelClient,
            options)
    {
    }

    public AgentOrchestrator(
        Func<string, IEnumerable<ITool>> toolFactory,
        AgentOrchestrationDependencies dependencies,
        IModelClient? modelClient = null,
        AgentOrchestratorOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(toolFactory);
        ArgumentNullException.ThrowIfNull(dependencies);

        _modelClient = modelClient;
        _options = options ?? new AgentOrchestratorOptions();
        _promptClarifier = dependencies.PromptClarifier;
        _instructionBuilder = dependencies.InstructionBuilder;
        _sessionContextService = dependencies.SessionContextService;
        _recoveryPolicy = dependencies.RecoveryPolicy;
        _toolOutputAdapter = dependencies.ToolOutputAdapter;
        _evidenceEvaluator = dependencies.EvidenceEvaluator;
        _convergencePolicy = dependencies.ConvergencePolicy;

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
            : ProcessModelDrivenAsync(request, null, cancellationToken);
    }

    public Task<AgentResponse> ProcessAsync(
        AgentRequest request,
        IProgress<AgentProgressEvent> progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(progress);

        return _modelClient is null
            ? ProcessRuleBasedAsync(request, cancellationToken)
            : ProcessModelDrivenAsync(request, progress, cancellationToken);
    }

    private async Task<AgentResponse> ProcessModelDrivenAsync(
        AgentRequest request,
        IProgress<AgentProgressEvent>? progress,
        CancellationToken cancellationToken)
    {
        var steps = new List<AgentExecutionStep>();
        var toolResults = new List<ToolExecutionResult>();

        if (!TryValidateRequest(request, steps, toolResults, out var invalidResponse))
        {
            return invalidResponse!;
        }

        var executionContext = await InitializeModelExecutionAsync(request, steps, cancellationToken);
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
        var evidenceItems = new List<EvidenceItem>();
        var recoveryState = RecoveryState.Empty;
        AggregatedEvidenceContext? aggregatedEvidenceContext = null;
        string? previousResponseId = null;
        var duplicateToolCallCount = 0;
        var consecutiveNoProgressCount = 0;
        ConvergenceEvaluation? latestConvergenceEvaluation = null;

        for (var iteration = 1; iteration <= _options.MaxIterations; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            steps.Add(new AgentExecutionStep($"Calling model for iteration {iteration}."));
            Report(progress, new StepProgressEvent($"Calling model for iteration {iteration}.", true));

            var modelRequest = BuildModelRequest(
                conversation,
                executionContext.RegisteredTools,
                sessionState,
                previousResponseId);
            var modelResponse = await _modelClient!.GenerateAsync(modelRequest, cancellationToken);
            previousResponseId = modelResponse.ResponseId;

            var toolCalls = modelResponse.ToolCalls?.Where(call => !string.IsNullOrWhiteSpace(call.ToolName)).ToArray()
                ?? Array.Empty<ModelToolCall>();

            if (ShouldAcceptFinalAnswerFromConvergence(modelResponse.FinalAnswer, toolCalls, latestConvergenceEvaluation))
            {
                steps.Add(new AgentExecutionStep(
                    $"Model returned a final answer on iteration {iteration}; convergence guidance allowed finalization."));
                Report(progress, new AnswerProgressEvent(modelResponse.FinalAnswer!));
                return BuildSuccessResponse(modelResponse.FinalAnswer!, steps, toolResults, evidenceItems, latestConvergenceEvaluation);
            }

            var finalAnswerDecision = _recoveryPolicy.EvaluateFinalAnswer(
                modelResponse.FinalAnswer,
                toolCalls,
                recoveryState,
                aggregatedEvidenceContext,
                request.UserPrompt);

            if (finalAnswerDecision.WasHandled)
            {
                if (finalAnswerDecision.ShouldFinalize)
                {
                    steps.Add(new AgentExecutionStep($"Model returned a final answer on iteration {iteration}."));
                    Report(progress, new AnswerProgressEvent(finalAnswerDecision.FinalAnswer!));
                    return BuildSuccessResponse(finalAnswerDecision.FinalAnswer!, steps, toolResults, evidenceItems, latestConvergenceEvaluation);
                }

                steps.Add(new AgentExecutionStep(
                    recoveryState.HasPendingWeakSearchEvidence
                        ? "Model attempted to finalize after weak search evidence; requesting broader recovery instead."
                        : "Model attempted to finalize before aggregating enough multi-file evidence; requesting one more supporting file.",
                    false));
                conversation.Add(new ModelTextMessage("user", finalAnswerDecision.FollowUpPrompt!));
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

            var searchFilesCallsThisIteration = 0;
            var readFileCallsThisIteration = 0;
            foreach (var toolCall in toolCalls)
            {
                if (ExceedsFetchBudget(
                    toolCall.ToolName,
                    ref searchFilesCallsThisIteration,
                    ref readFileCallsThisIteration))
                {
                    consecutiveNoProgressCount++;
                    latestConvergenceEvaluation = EvaluateConvergence(
                        evidenceItems,
                        iteration,
                        duplicateToolCallCount,
                        consecutiveNoProgressCount,
                        hadToolFailure: false,
                        newEvidenceCount: 0);
                    sessionState = await HandleFetchBudgetExceededAsync(
                        toolCall,
                        sessionState,
                        conversation,
                        steps,
                        latestConvergenceEvaluation,
                        cancellationToken);
                    continue;
                }

                var toolCallSignature = CreateToolCallSignature(toolCall);
                if (!executedToolCalls.Add(toolCallSignature))
                {
                    duplicateToolCallCount++;
                    consecutiveNoProgressCount++;
                    latestConvergenceEvaluation = EvaluateConvergence(
                        evidenceItems,
                        iteration,
                        duplicateToolCallCount,
                        consecutiveNoProgressCount,
                        hadToolFailure: false,
                        newEvidenceCount: 0);
                    sessionState = await HandleDuplicateToolCallAsync(
                        toolCall,
                        sessionState,
                        conversation,
                        steps,
                        latestConvergenceEvaluation,
                        cancellationToken);
                    continue;
                }

                var executionResult = await ExecuteToolCallAsync(
                    executionContext.Tools,
                    toolCall,
                    steps,
                    cancellationToken);
                toolResults.Add(executionResult);
                Report(progress, new ToolResultProgressEvent(toolCall.ToolName, executionResult.Success, executionResult.ErrorMessage));

                if (executionResult.Success)
                {
                    recoveryState = _recoveryPolicy.UpdateWeakSearchState(
                        recoveryState,
                        toolCall.ToolName,
                        executionResult.Output,
                        request.UserPrompt);
                }

                var outputResult = _toolOutputAdapter.BuildToolConversationOutput(
                    toolCall,
                    executionResult,
                    request.UserPrompt,
                    aggregatedEvidenceContext);
                aggregatedEvidenceContext = outputResult.AggregatedEvidenceContext;

                var newEvidenceCount = outputResult.EvidenceItems.Count;
                evidenceItems.AddRange(outputResult.EvidenceItems);
                consecutiveNoProgressCount = executionResult.Success && newEvidenceCount > 0
                    ? 0
                    : consecutiveNoProgressCount + 1;
                latestConvergenceEvaluation = EvaluateConvergence(
                    evidenceItems,
                    iteration,
                    duplicateToolCallCount,
                    consecutiveNoProgressCount,
                    hadToolFailure: !executionResult.Success,
                    newEvidenceCount);

                var modelFacingOutput = AppendConvergenceGuidance(
                    outputResult.Output,
                    latestConvergenceEvaluation);
                steps.Add(new AgentExecutionStep(
                    $"Convergence guidance: {latestConvergenceEvaluation.Decision.DecisionType}."));
                Report(progress, new StepProgressEvent($"Convergence: {latestConvergenceEvaluation.Decision.DecisionType}.", true));


                conversation.Add(new ModelToolCallMessage(toolCall));
                conversation.Add(new ModelToolResultMessage(toolCall.CallId, toolCall.ToolName, modelFacingOutput));
                sessionState = await _sessionContextService.UpdateAsync(
                    sessionState,
                    toolCall.ToolName,
                    outputResult.Output,
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
        var sessionState = await _sessionContextService.LoadOrCreateAsync(request, cancellationToken);

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

    private ModelRequest BuildModelRequest(
        IReadOnlyCollection<ModelConversationItem> conversation,
        IReadOnlyCollection<ToolDefinition> registeredTools,
        AgentSessionState? sessionState,
        string? previousResponseId)
    {
        return new ModelRequest(
            _instructionBuilder.BuildInstructions(sessionState),
            conversation,
            registeredTools
                .Select(tool => new ModelToolDefinition(tool.Name, tool.Description, tool.Parameters))
                .ToArray(),
            previousResponseId);
    }

    private async Task<AgentSessionState?> HandleDuplicateToolCallAsync(
        ModelToolCall toolCall,
        AgentSessionState? sessionState,
        ICollection<ModelConversationItem> conversation,
        ICollection<AgentExecutionStep> steps,
        ConvergenceEvaluation? convergenceEvaluation,
        CancellationToken cancellationToken)
    {
        var duplicateToolCallMessage = _recoveryPolicy.BuildDuplicateToolCallMessage(toolCall, sessionState);
        var modelFacingMessage = AppendConvergenceGuidance(
            duplicateToolCallMessage,
            convergenceEvaluation);

        steps.Add(new AgentExecutionStep(
            $"Prevented duplicate tool call '{toolCall.ToolName}' with the same arguments.",
            false));

        conversation.Add(new ModelToolCallMessage(toolCall));
        conversation.Add(new ModelToolResultMessage(
            toolCall.CallId,
            toolCall.ToolName,
            modelFacingMessage));

        return await _sessionContextService.UpdateAsync(
            sessionState,
            toolCall.ToolName,
            duplicateToolCallMessage,
            null,
            cancellationToken);
    }

    private async Task<AgentSessionState?> HandleFetchBudgetExceededAsync(
        ModelToolCall toolCall,
        AgentSessionState? sessionState,
        ICollection<ModelConversationItem> conversation,
        ICollection<AgentExecutionStep> steps,
        ConvergenceEvaluation convergenceEvaluation,
        CancellationToken cancellationToken)
    {
        var budgetMessage = BuildFetchBudgetExceededMessage(toolCall.ToolName);
        var modelFacingMessage = AppendConvergenceGuidance(
            budgetMessage,
            convergenceEvaluation);

        steps.Add(new AgentExecutionStep(
            $"Skipped tool call '{toolCall.ToolName}' because the per-iteration fetch budget was reached.",
            false));

        conversation.Add(new ModelToolCallMessage(toolCall));
        conversation.Add(new ModelToolResultMessage(
            toolCall.CallId,
            toolCall.ToolName,
            modelFacingMessage));

        return await _sessionContextService.UpdateAsync(
            sessionState,
            toolCall.ToolName,
            budgetMessage,
            null,
            cancellationToken);
    }

    private ConvergenceEvaluation EvaluateConvergence(
        IReadOnlyCollection<EvidenceItem> evidenceItems,
        int iteration,
        int duplicateToolCallCount,
        int consecutiveNoProgressCount,
        bool hadToolFailure,
        int newEvidenceCount)
    {
        var assessment = _evidenceEvaluator.Assess(evidenceItems);
        var decision = _convergencePolicy.Evaluate(
            assessment,
            evidenceItems,
            new ConvergenceContext(
                iteration,
                duplicateToolCallCount,
                consecutiveNoProgressCount,
                hadToolFailure,
                newEvidenceCount));
        return new ConvergenceEvaluation(assessment, decision);
    }

    private static string AppendConvergenceGuidance(
        string output,
        ConvergenceEvaluation? convergenceEvaluation)
    {
        if (convergenceEvaluation is null)
        {
            return output;
        }

        var builder = new StringBuilder(output.TrimEnd());
        if (builder.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine();
        }

        builder.Append(BuildConvergenceGuidance(convergenceEvaluation));
        return builder.ToString();
    }

    private static bool ShouldAcceptFinalAnswerFromConvergence(
        string? finalAnswer,
        IReadOnlyCollection<ModelToolCall> toolCalls,
        ConvergenceEvaluation? convergenceEvaluation)
    {
        if (string.IsNullOrWhiteSpace(finalAnswer) || toolCalls.Count > 0)
        {
            return false;
        }

        return convergenceEvaluation?.Decision.DecisionType is
            ConvergenceDecisionType.FinalizePartialAnswer or
            ConvergenceDecisionType.FinalizeConfidentAnswer;
    }

    private static bool ExceedsFetchBudget(
        string toolName,
        ref int searchFilesCallsThisIteration,
        ref int readFileCallsThisIteration)
    {
        if (string.Equals(toolName, SearchFilesToolName, StringComparison.OrdinalIgnoreCase))
        {
            searchFilesCallsThisIteration++;
            return searchFilesCallsThisIteration > MaxSearchFilesCallsPerIteration;
        }

        if (string.Equals(toolName, ReadFileToolName, StringComparison.OrdinalIgnoreCase))
        {
            readFileCallsThisIteration++;
            return readFileCallsThisIteration > MaxReadFileCallsPerIteration;
        }

        return false;
    }

    private static string BuildFetchBudgetExceededMessage(string toolName)
    {
        var toolGuidance = string.Equals(toolName, ReadFileToolName, StringComparison.OrdinalIgnoreCase)
            ? "Avoid reading too many files. Focus on the most relevant sources."
            : "Avoid running too many searches. Focus on the strongest query or change strategy.";

        return $"""
            Fetch budget reached for this iteration.
            {toolGuidance}
            The skipped tool call was not executed.
            """;
    }

    private static string BuildConvergenceGuidance(ConvergenceEvaluation convergenceEvaluation)
    {
        var assessment = convergenceEvaluation.Assessment;
        var decision = convergenceEvaluation.Decision;
        var builder = new StringBuilder();
        builder.AppendLine("Current Evidence State:");
        builder.AppendLine($"- Sufficiency: {(assessment.IsSufficient ? "sufficient" : "insufficient")}");
        builder.AppendLine($"- Coverage: {DescribeScore(assessment.CoverageScore)} ({assessment.CoverageScore:F2})");
        builder.AppendLine($"- Confidence: {DescribeScore(assessment.ConfidenceScore)} ({assessment.ConfidenceScore:F2})");
        builder.AppendLine($"- Reason: {assessment.Reason}");
        builder.AppendLine();
        builder.AppendLine("Convergence Guidance:");
        builder.AppendLine($"- Preferred Next Action: {decision.DecisionType}");
        builder.AppendLine("- Avoid: repeating the same tool call");
        builder.AppendLine("- If previous tool call did not produce new information, change strategy.");
        builder.AppendLine("- Prefer finalizing a partial answer instead of looping.");
        if (decision.DecisionType == ConvergenceDecisionType.FinalizePartialAnswer)
        {
            builder.AppendLine("- STOP further tool calls if no meaningful new evidence is found.");
            builder.AppendLine("- Provide a partial answer with clear limitations.");
        }

        var actionGuidance = MapDecisionToGuidance(decision);
        if (!string.IsNullOrWhiteSpace(actionGuidance))
        {
            builder.AppendLine($"- Guidance: {actionGuidance}");
        }

        if (!string.IsNullOrWhiteSpace(decision.Guidance))
        {
            builder.AppendLine($"- Policy Note: {decision.Guidance}");
        }

        return builder.ToString().TrimEnd();
    }

    private static string DescribeScore(double score)
    {
        return score switch
        {
            >= 0.75 => "high",
            >= 0.5 => "moderate",
            > 0 => "limited",
            _ => "none"
        };
    }

    private static string MapDecisionToGuidance(ConvergenceDecision decision)
    {
        return decision.DecisionType switch
        {
            ConvergenceDecisionType.ContinueWithBroaderSearch =>
                "Explore different files or broader related search terms before finalizing.",
            ConvergenceDecisionType.ContinueWithDeeperRead =>
                "Read the most relevant candidate file in more depth before making file-level claims.",
            ConvergenceDecisionType.FinalizePartialAnswer =>
                "Answer with explicit limitations; do not continue unnecessary tool calls. STOP further tool calls if no meaningful new evidence is found.",
            ConvergenceDecisionType.FinalizeConfidentAnswer =>
                "Generate a final answer now and ground it in the observed evidence.",
            _ => decision.Guidance ?? string.Empty
        };
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

    private AgentResponse BuildSuccessResponse(
        string output,
        IEnumerable<AgentExecutionStep> steps,
        IReadOnlyCollection<ToolExecutionResult> toolResults,
        IReadOnlyList<EvidenceItem> evidenceItems,
        ConvergenceEvaluation? convergenceEvaluation)
    {
        var domainItems = evidenceItems
            .Select(e => new AgentEvidenceItem(e.ToolName, e.SourceId, e.Content, e.Kind.ToString(), e.IsPartial, e.Confidence))
            .ToArray();

        AgentEvidenceAssessment? domainAssessment = null;
        if (convergenceEvaluation is not null)
        {
            var assessment = convergenceEvaluation.Assessment;
            domainAssessment = new AgentEvidenceAssessment(
                assessment.IsSufficient,
                assessment.CoverageScore,
                assessment.ConfidenceScore,
                assessment.Reason,
                assessment.MissingAreas);
        }
        else if (evidenceItems.Count > 0)
        {
            var assessment = _evidenceEvaluator.Assess(evidenceItems);
            domainAssessment = new AgentEvidenceAssessment(
                assessment.IsSufficient,
                assessment.CoverageScore,
                assessment.ConfidenceScore,
                assessment.Reason,
                assessment.MissingAreas);
        }

        return new AgentResponse(
            Output: output,
            ExecutionSteps: steps.ToArray(),
            ToolResults: toolResults,
            EvidenceItems: domainItems.Length > 0 ? domainItems : null,
            FinalAssessment: domainAssessment);
    }

    private static void Report(IProgress<AgentProgressEvent>? progress, AgentProgressEvent evt)
    {
        progress?.Report(evt);
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

    private sealed record ConvergenceEvaluation(
        EvidenceAssessment Assessment,
        ConvergenceDecision Decision);
}
