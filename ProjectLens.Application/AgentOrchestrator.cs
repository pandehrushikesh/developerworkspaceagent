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
    private readonly IModelClient? _modelClient;
    private readonly AgentOrchestratorOptions _options;
    private readonly IAgentSessionStore? _sessionStore;
    private readonly ISessionSummarizer? _sessionSummarizer;
    private readonly Func<string, IReadOnlyDictionary<string, ITool>> _toolFactory;

    public AgentOrchestrator(
        IEnumerable<ITool> tools,
        IModelClient? modelClient = null,
        AgentOrchestratorOptions? options = null,
        IAgentSessionStore? sessionStore = null,
        IFileCompressor? fileCompressor = null,
        ISessionSummarizer? sessionSummarizer = null)
        : this(_ => tools, modelClient, options, sessionStore, fileCompressor, sessionSummarizer)
    {
    }

    public AgentOrchestrator(
        Func<string, IEnumerable<ITool>> toolFactory,
        IModelClient? modelClient = null,
        AgentOrchestratorOptions? options = null,
        IAgentSessionStore? sessionStore = null,
        IFileCompressor? fileCompressor = null,
        ISessionSummarizer? sessionSummarizer = null)
    {
        ArgumentNullException.ThrowIfNull(toolFactory);

        _fileCompressor = fileCompressor;
        _modelClient = modelClient;
        _options = options ?? new AgentOrchestratorOptions();
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

        var conversation = new List<ModelConversationItem>
        {
            new ModelTextMessage("user", request.UserPrompt)
        };
        var executedToolCalls = new HashSet<string>(StringComparer.Ordinal);
        string? previousResponseId = null;

        for (var iteration = 1; iteration <= _options.MaxIterations; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            steps.Add(new AgentExecutionStep($"Calling model for iteration {iteration}."));

            var modelRequest = new ModelRequest(
                BuildModelInstructions(sessionState),
                conversation.ToArray(),
                registeredTools
                    .Select(tool => new ModelToolDefinition(tool.Name, tool.Description, tool.Parameters))
                    .ToArray(),
                previousResponseId);

            var modelResponse = await _modelClient!.GenerateAsync(modelRequest, cancellationToken);
            previousResponseId = modelResponse.ResponseId;

            var toolCalls = modelResponse.ToolCalls?.Where(call => !string.IsNullOrWhiteSpace(call.ToolName)).ToArray()
                ?? Array.Empty<ModelToolCall>();

            if (!string.IsNullOrWhiteSpace(modelResponse.FinalAnswer) && toolCalls.Length == 0)
            {
                steps.Add(new AgentExecutionStep($"Model returned a final answer on iteration {iteration}."));
                return new AgentResponse(modelResponse.FinalAnswer, steps, toolResults);
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
                    var duplicateToolCallMessage = BuildDuplicateToolCallMessage(toolCall, sessionState);

                    steps.Add(new AgentExecutionStep(
                        $"Prevented duplicate tool call '{toolCall.ToolName}' with the same arguments.",
                        false));

                    conversation.Add(new ModelToolResultMessage(
                        toolCall.CallId,
                        toolCall.ToolName,
                        duplicateToolCallMessage));

                    sessionState = await UpdateSessionStateAsync(
                        sessionState,
                        toolCall.ToolName,
                        duplicateToolCallMessage,
                        null,
                        cancellationToken);

                    continue;
                }

                var executionResult = await ExecuteToolCallAsync(tools, toolCall, steps, cancellationToken);
                toolResults.Add(executionResult);

                var output = executionResult.Success
                    ? CreateToolContextOutput(toolCall.ToolName, executionResult.Output, request.UserPrompt)
                    : executionResult.ErrorMessage ?? "Tool execution failed.";

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
            ExtractVisitedFiles(toolName, rawToolOutput),
            20);

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
            For follow-up requests about refactoring, improving, or explaining logic you already inspected, use the existing session context and prior file summaries first.
            If the session already identifies a likely file or flow, propose the best grounded answer or refactor direction you can before requesting more tool calls.
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
        string userPrompt)
    {
        if (string.IsNullOrWhiteSpace(rawToolOutput))
        {
            return string.Empty;
        }

        if (string.Equals(toolName, ReadFileToolName, StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseReadFilePayload(rawToolOutput, out var path, out var content))
            {
                return rawToolOutput;
            }

            return _fileCompressor?.Compress(path, content, userPrompt) ?? rawToolOutput;
        }

        if (string.Equals(toolName, SearchFilesToolName, StringComparison.OrdinalIgnoreCase))
        {
            return SummarizeSearchResults(rawToolOutput);
        }

        return rawToolOutput;
    }

    private static string SummarizeSearchResults(string rawToolOutput)
    {
        try
        {
            using var document = JsonDocument.Parse(rawToolOutput);
            var root = document.RootElement;
            var query = root.TryGetProperty("Query", out var queryElement) ? queryElement.GetString() : null;
            var totalMatches = root.TryGetProperty("TotalMatches", out var totalMatchesElement)
                ? totalMatchesElement.GetInt32()
                : 0;
            var matchLines = root.TryGetProperty("Matches", out var matchesElement)
                ? matchesElement.EnumerateArray()
                    .Take(5)
                    .Select(match =>
                    {
                        var path = match.TryGetProperty("Path", out var pathElement) ? pathElement.GetString() : null;
                        var snippet = match.TryGetProperty("Snippet", out var snippetElement) ? snippetElement.GetString() : null;
                        return string.IsNullOrWhiteSpace(path)
                            ? null
                            : $"{path}: {snippet}";
                    })
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .ToArray()
                : Array.Empty<string>();

            var builder = new StringBuilder();
            builder.AppendLine($"search_files query: {query}");
            builder.AppendLine($"Total matches: {totalMatches}");

            foreach (var matchLine in matchLines)
            {
                builder.AppendLine($"- {matchLine}");
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
        out string content)
    {
        path = string.Empty;
        content = string.Empty;

        try
        {
            using var document = JsonDocument.Parse(rawToolOutput);
            var root = document.RootElement;
            path = root.GetProperty("Path").GetString() ?? string.Empty;
            content = root.GetProperty("Content").GetString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(path);
        }
        catch
        {
            return false;
        }
    }

    private static IReadOnlyCollection<string> ExtractVisitedFiles(string toolName, string? rawToolOutput)
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
                    : [path];
            }

            if (string.Equals(toolName, SearchFilesToolName, StringComparison.OrdinalIgnoreCase) &&
                root.TryGetProperty("Matches", out var matchesElement))
            {
                return matchesElement
                    .EnumerateArray()
                    .Take(5)
                    .Select(match => match.TryGetProperty("Path", out var pathElement) ? pathElement.GetString() : null)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Cast<string>()
                    .ToArray();
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
}
