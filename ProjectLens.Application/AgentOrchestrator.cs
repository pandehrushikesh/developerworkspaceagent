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

    private readonly IModelClient? _modelClient;
    private readonly AgentOrchestratorOptions _options;
    private readonly Func<string, IReadOnlyDictionary<string, ITool>> _toolFactory;

    public AgentOrchestrator(
        IEnumerable<ITool> tools,
        IModelClient? modelClient = null,
        AgentOrchestratorOptions? options = null)
        : this(_ => tools, modelClient, options)
    {
    }

    public AgentOrchestrator(
        Func<string, IEnumerable<ITool>> toolFactory,
        IModelClient? modelClient = null,
        AgentOrchestratorOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(toolFactory);

        _modelClient = modelClient;
        _options = options ?? new AgentOrchestratorOptions();
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

        steps.Add(new AgentExecutionStep(
            $"Registered tools: {string.Join(", ", registeredTools.Select(tool => tool.Name))}"));

        var conversation = new List<ModelConversationItem>
        {
            new ModelTextMessage("user", request.UserPrompt)
        };

        for (var iteration = 1; iteration <= _options.MaxIterations; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            steps.Add(new AgentExecutionStep($"Calling model for iteration {iteration}."));

            var modelRequest = new ModelRequest(
                BuildModelInstructions(),
                conversation.ToArray(),
                registeredTools
                    .Select(tool => new ModelToolDefinition(tool.Name, tool.Description, tool.Parameters))
                    .ToArray());

            var modelResponse = await _modelClient!.GenerateAsync(modelRequest, cancellationToken);

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
                var executionResult = await ExecuteToolCallAsync(tools, toolCall, steps, cancellationToken);
                toolResults.Add(executionResult);

                var output = executionResult.Success
                    ? executionResult.Output ?? string.Empty
                    : executionResult.ErrorMessage ?? "Tool execution failed.";

                conversation.Add(new ModelToolResultMessage(toolCall.CallId, toolCall.ToolName, output));
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

    private static string BuildModelInstructions()
    {
        return """
            You are ProjectLens, a repository analysis agent.
            You must answer using only:
            - the user's request
            - explicit tool outputs already present in the conversation

            Do not use outside knowledge or make up file contents, project details, or tool results.
            If you need more information, request a tool call using one of the provided tools.
            Use tool calls only when they materially help answer the user's request.
            When you give a final answer, ground every claim in either the user's request or prior tool outputs.
            If the available evidence is insufficient, say that directly.
            """;
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
