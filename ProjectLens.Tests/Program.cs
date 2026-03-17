using ProjectLens.Application;
using ProjectLens.Application.Abstractions;
using ProjectLens.Domain;
using ProjectLens.Infrastructure.Tools;
using ProjectLens.Infrastructure.Tools.Models;
using System.Text.Json;

namespace ProjectLens.Tests;

internal static class Program
{
    public static async Task<int> Main()
    {
        var testCases = new (string Name, Func<Task> Run)[]
        {
            ("ListFilesTool lists direct children", ToolTests.ListFilesToolListsDirectChildrenAsync),
            ("ListFilesTool respects recursion depth", ToolTests.ListFilesToolRespectsMaxDepthAsync),
            ("ReadFileTool reads and truncates text", ToolTests.ReadFileToolReadsAndTruncatesTextAsync),
            ("ReadFileTool rejects files outside workspace", ToolTests.ReadFileToolRejectsFilesOutsideWorkspaceAsync),
            ("ReadFileTool rejects binary files", ToolTests.ReadFileToolRejectsBinaryFilesAsync),
            ("SearchFilesTool requires a query", ToolTests.SearchFilesToolRequiresQueryAsync),
            ("SearchFilesTool rejects invalid paths", ToolTests.SearchFilesToolRejectsInvalidPathAsync),
            ("SearchFilesTool finds matches in a single file", ToolTests.SearchFilesToolFindsMatchesInSingleFileAsync),
            ("SearchFilesTool finds matches recursively", ToolTests.SearchFilesToolFindsMatchesRecursivelyAsync),
            ("SearchFilesTool honors file patterns", ToolTests.SearchFilesToolHonorsFilePatternAsync),
            ("SearchFilesTool honors max results", ToolTests.SearchFilesToolHonorsMaxResultsAsync),
            ("SearchFilesTool honors case sensitivity", ToolTests.SearchFilesToolHonorsCaseSensitivityAsync),
            ("SearchFilesTool skips binary files", ToolTests.SearchFilesToolSkipsBinaryFilesAsync),
            ("SearchFilesTool returns stable readable snippets", ToolTests.SearchFilesToolReturnsReadableSnippetsAsync),
            ("AgentOrchestrator summarizes README and project file", ToolTests.AgentOrchestratorSummarizesWorkspaceAsync),
            ("AgentOrchestrator handles missing optional files", ToolTests.AgentOrchestratorHandlesMissingWorkspaceFilesAsync),
            ("AgentOrchestrator requires registered tools", ToolTests.AgentOrchestratorRequiresRegisteredToolsAsync),
            ("AgentOrchestrator returns final answer without tool call", ToolTests.AgentOrchestratorReturnsFinalAnswerWithoutToolCallAsync),
            ("AgentOrchestrator handles a single tool call", ToolTests.AgentOrchestratorHandlesSingleToolCallAsync),
            ("AgentOrchestrator handles multiple tool calls", ToolTests.AgentOrchestratorHandlesMultipleToolCallsAsync),
            ("AgentOrchestrator stops at max iterations", ToolTests.AgentOrchestratorStopsAtMaxIterationsAsync)
        };

        var failures = new List<string>();

        foreach (var (name, run) in testCases)
        {
            try
            {
                await run();
                Console.WriteLine($"PASS {name}");
            }
            catch (Exception exception)
            {
                failures.Add($"{name}: {exception.Message}");
                Console.WriteLine($"FAIL {name}");
            }
        }

        if (failures.Count == 0)
        {
            Console.WriteLine($"Executed {testCases.Length} tests successfully.");
            return 0;
        }

        Console.WriteLine();
        Console.WriteLine("Failures:");
        foreach (var failure in failures)
        {
            Console.WriteLine(failure);
        }

        return 1;
    }
}

internal static class ToolTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static async Task ListFilesToolListsDirectChildrenAsync()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteText("alpha.txt", "hello");
        workspace.WriteText(Path.Combine("nested", "beta.txt"), "world");

        var tool = new ListFilesTool(workspace.RootPath);
        var result = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["path"] = ".",
            ["recursive"] = "false"
        });

        TestAssert.True(result.Success, "The tool should succeed.");
        var response = Deserialize<ListFilesResponse>(result.Output);

        TestAssert.Equal(".", response.RootPath);
        TestAssert.SequenceEqual(
            new[]
            {
                new WorkspaceEntry("alpha.txt", false),
                new WorkspaceEntry("nested", true)
            },
            response.Entries.OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public static async Task ListFilesToolRespectsMaxDepthAsync()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteText(Path.Combine("level1", "file1.txt"), "one");
        workspace.WriteText(Path.Combine("level1", "level2", "file2.txt"), "two");
        workspace.WriteText(Path.Combine("level1", "level2", "level3", "file3.txt"), "three");

        var tool = new ListFilesTool(workspace.RootPath);
        var result = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["path"] = "level1",
            ["recursive"] = "true",
            ["maxDepth"] = "2"
        });

        TestAssert.True(result.Success, "The tool should succeed.");
        var response = Deserialize<ListFilesResponse>(result.Output);

        TestAssert.SequenceEqual(
            new[]
            {
                new WorkspaceEntry("level1/file1.txt", false),
                new WorkspaceEntry("level1/level2", true),
                new WorkspaceEntry("level1/level2/file2.txt", false),
                new WorkspaceEntry("level1/level2/level3", true)
            },
            response.Entries.OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public static async Task ReadFileToolReadsAndTruncatesTextAsync()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteText("notes.txt", "abcdefghijklmnopqrstuvwxyz");

        var tool = new ReadFileTool(workspace.RootPath, maxCharacters: 10);
        var result = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["path"] = "notes.txt"
        });

        TestAssert.True(result.Success, "The tool should succeed.");
        var response = Deserialize<ReadFileResponse>(result.Output);

        TestAssert.Equal("notes.txt", response.Path);
        TestAssert.Equal("abcdefghij", response.Content);
        TestAssert.True(response.IsTruncated, "The content should be truncated.");
        TestAssert.Equal(10, response.CharacterCount);
    }

    public static async Task ReadFileToolRejectsFilesOutsideWorkspaceAsync()
    {
        using var workspace = new TestWorkspace();
        using var outsideDirectory = new TestWorkspace();
        outsideDirectory.WriteText("outside.txt", "nope");

        var tool = new ReadFileTool(workspace.RootPath, maxCharacters: 100);
        var result = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["path"] = Path.Combine(outsideDirectory.RootPath, "outside.txt")
        });

        TestAssert.False(result.Success, "The tool should reject files outside the workspace.");
        TestAssert.Contains("outside the workspace root", result.ErrorMessage);
    }

    public static async Task ReadFileToolRejectsBinaryFilesAsync()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteBinary("data.bin", new byte[] { 0, 159, 255, 42 });

        var tool = new ReadFileTool(workspace.RootPath, maxCharacters: 100);
        var result = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["path"] = "data.bin"
        });

        TestAssert.False(result.Success, "The tool should reject binary files.");
        TestAssert.Contains("Only text-based files can be read.", result.ErrorMessage);
    }

    public static async Task SearchFilesToolRequiresQueryAsync()
    {
        using var workspace = new TestWorkspace();
        var tool = new SearchFilesTool(workspace.RootPath);

        var result = await tool.ExecuteAsync(new Dictionary<string, string>());

        TestAssert.False(result.Success, "The tool should reject missing queries.");
        TestAssert.Contains("The query argument is required.", result.ErrorMessage);
    }

    public static async Task SearchFilesToolRejectsInvalidPathAsync()
    {
        using var workspace = new TestWorkspace();
        var tool = new SearchFilesTool(workspace.RootPath);

        var result = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["query"] = "ProjectLens",
            ["path"] = "missing"
        });

        TestAssert.False(result.Success, "The tool should reject missing paths.");
        TestAssert.Contains("The requested path does not exist.", result.ErrorMessage);
    }

    public static async Task SearchFilesToolFindsMatchesInSingleFileAsync()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteText(
            "README.md",
            """
            intro
            ProjectLens helps inspect repositories.
            closing
            """);

        var tool = new SearchFilesTool(workspace.RootPath);
        var result = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["query"] = "ProjectLens",
            ["path"] = "README.md"
        });

        TestAssert.True(result.Success, "The tool should succeed.");
        var response = Deserialize<SearchFilesResponse>(result.Output);

        TestAssert.Equal("README.md", response.SearchRoot);
        TestAssert.Equal(1, response.TotalMatches);
        TestAssert.SequenceEqual(
            new[]
            {
                new SearchFileMatch("README.md", 2, "ProjectLens helps inspect repositories.")
            },
            response.Matches.ToArray());
    }

    public static async Task SearchFilesToolFindsMatchesRecursivelyAsync()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteText(Path.Combine("src", "alpha.txt"), "ProjectLens alpha");
        workspace.WriteText(Path.Combine("src", "nested", "beta.txt"), "beta\nProjectLens nested");
        workspace.WriteText(Path.Combine("src", "gamma.txt"), "no match");

        var tool = new SearchFilesTool(workspace.RootPath);
        var result = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["query"] = "ProjectLens",
            ["path"] = "src"
        });

        TestAssert.True(result.Success, "The tool should succeed.");
        var response = Deserialize<SearchFilesResponse>(result.Output);

        TestAssert.Equal("src", response.SearchRoot);
        TestAssert.Equal(2, response.TotalMatches);
        TestAssert.SequenceEqual(
            new[]
            {
                new SearchFileMatch("src/alpha.txt", 1, "ProjectLens alpha"),
                new SearchFileMatch("src/nested/beta.txt", 2, "ProjectLens nested")
            },
            response.Matches.ToArray());
    }

    public static async Task SearchFilesToolHonorsFilePatternAsync()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteText("notes.txt", "ProjectLens text");
        workspace.WriteText("notes.md", "ProjectLens markdown");

        var tool = new SearchFilesTool(workspace.RootPath);
        var result = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["query"] = "ProjectLens",
            ["filePattern"] = "*.md"
        });

        TestAssert.True(result.Success, "The tool should succeed.");
        var response = Deserialize<SearchFilesResponse>(result.Output);

        TestAssert.Equal(1, response.TotalMatches);
        TestAssert.SequenceEqual(
            new[]
            {
                new SearchFileMatch("notes.md", 1, "ProjectLens markdown")
            },
            response.Matches.ToArray());
    }

    public static async Task SearchFilesToolHonorsMaxResultsAsync()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteText("a.txt", "ProjectLens one");
        workspace.WriteText("b.txt", "ProjectLens two");
        workspace.WriteText("c.txt", "ProjectLens three");

        var tool = new SearchFilesTool(workspace.RootPath);
        var result = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["query"] = "ProjectLens",
            ["maxResults"] = "2"
        });

        TestAssert.True(result.Success, "The tool should succeed.");
        var response = Deserialize<SearchFilesResponse>(result.Output);

        TestAssert.Equal(2, response.TotalMatches);
        TestAssert.SequenceEqual(
            new[]
            {
                new SearchFileMatch("a.txt", 1, "ProjectLens one"),
                new SearchFileMatch("b.txt", 1, "ProjectLens two")
            },
            response.Matches.ToArray());
    }

    public static async Task SearchFilesToolHonorsCaseSensitivityAsync()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteText(
            "cases.txt",
            """
            projectlens lower
            ProjectLens exact
            """);

        var tool = new SearchFilesTool(workspace.RootPath);

        var insensitiveResult = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["query"] = "projectlens"
        });

        var sensitiveResult = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["query"] = "projectlens",
            ["caseSensitive"] = "true"
        });

        TestAssert.True(insensitiveResult.Success, "Case-insensitive search should succeed.");
        TestAssert.True(sensitiveResult.Success, "Case-sensitive search should succeed.");

        var insensitiveResponse = Deserialize<SearchFilesResponse>(insensitiveResult.Output);
        var sensitiveResponse = Deserialize<SearchFilesResponse>(sensitiveResult.Output);

        TestAssert.Equal(2, insensitiveResponse.TotalMatches);
        TestAssert.Equal(1, sensitiveResponse.TotalMatches);
        TestAssert.SequenceEqual(
            new[]
            {
                new SearchFileMatch("cases.txt", 1, "projectlens lower")
            },
            sensitiveResponse.Matches.ToArray());
    }

    public static async Task SearchFilesToolSkipsBinaryFilesAsync()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteBinary("data.bin", new byte[] { 0, 1, 2, 3 });
        workspace.WriteText("notes.txt", "ProjectLens text");

        var tool = new SearchFilesTool(workspace.RootPath);
        var result = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["query"] = "ProjectLens"
        });

        TestAssert.True(result.Success, "The tool should succeed.");
        var response = Deserialize<SearchFilesResponse>(result.Output);

        TestAssert.Equal(1, response.TotalMatches);
        TestAssert.SequenceEqual(
            new[]
            {
                new SearchFileMatch("notes.txt", 1, "ProjectLens text")
            },
            response.Matches.ToArray());
    }

    public static async Task SearchFilesToolReturnsReadableSnippetsAsync()
    {
        using var workspace = new TestWorkspace();
        var longLine = "prefix " + new string('x', 220) + " ProjectLens " + new string('y', 220);
        workspace.WriteText("snippets.txt", $"   {longLine}   ");

        var tool = new SearchFilesTool(workspace.RootPath);
        var result = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["query"] = "ProjectLens"
        });

        TestAssert.True(result.Success, "The tool should succeed.");
        var response = Deserialize<SearchFilesResponse>(result.Output);
        var match = response.Matches.Single();

        TestAssert.Equal(1, response.TotalMatches);
        TestAssert.True(match.Snippet.Length <= 160, "The snippet should be trimmed to a readable length.");
        TestAssert.True(match.Snippet.EndsWith("...", StringComparison.Ordinal), "Long snippets should be truncated.");
        TestAssert.False(match.Snippet.StartsWith(" ", StringComparison.Ordinal), "The snippet should be trimmed.");
        TestAssert.Contains("prefix", match.Snippet);
    }

    public static async Task AgentOrchestratorSummarizesWorkspaceAsync()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteText("README.md", "ProjectLens helps inspect repositories and explain what is inside.");
        workspace.WriteText(
            Path.Combine("src", "ProjectLens.Host", "ProjectLens.Host.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        workspace.WriteText(Path.Combine("src", "ProjectLens.Host", "Program.cs"), "Console.WriteLine(\"hello\");");

        var orchestrator = CreateOrchestrator(workspace.RootPath);
        var response = await orchestrator.ProcessAsync(
            new AgentRequest("Summarize this repository", workspace.RootPath));

        TestAssert.True(response.Success, "The orchestrator should succeed.");
        TestAssert.Contains("ProjectLens helps inspect repositories", response.Output);
        TestAssert.Contains("targets net8.0", response.Output);
        TestAssert.Contains("outputs Exe", response.Output);
        TestAssert.Equal(3, response.ToolResults?.Count ?? 0);
        TestAssert.True(
            (response.ExecutionSteps?.Count ?? 0) >= 4,
            "The orchestrator should capture execution steps.");
        TestAssert.True(
            response.ExecutionSteps?.Any(step => step.Description.Contains("Registered tools", StringComparison.Ordinal)) == true,
            "The orchestrator should record the registered tools.");
    }

    public static async Task AgentOrchestratorHandlesMissingWorkspaceFilesAsync()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteText("notes.txt", "Only notes are present here.");

        var orchestrator = CreateOrchestrator(workspace.RootPath);
        var response = await orchestrator.ProcessAsync(
            new AgentRequest("Summarize this repository", workspace.RootPath));

        TestAssert.True(response.Success, "The orchestrator should still succeed.");
        TestAssert.Contains("README.md was not available", response.Output);
        TestAssert.Contains("No .csproj file was available", response.Output);
        TestAssert.Equal(1, response.ToolResults?.Count ?? 0);
    }

    public static async Task AgentOrchestratorRequiresRegisteredToolsAsync()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteText("README.md", "readme");

        var orchestrator = new AgentOrchestrator(Array.Empty<ITool>());
        var response = await orchestrator.ProcessAsync(
            new AgentRequest("Summarize this repository", workspace.RootPath));

        TestAssert.False(response.Success, "The orchestrator should fail without the required tools.");
        TestAssert.Contains("list_files", response.ErrorMessage);
    }

    public static async Task AgentOrchestratorReturnsFinalAnswerWithoutToolCallAsync()
    {
        using var workspace = new TestWorkspace();
        var modelClient = new ScriptedModelClient(request =>
        {
            TestAssert.Equal(1, request.Conversation.Count);
            return new ModelResponse("Grounded answer without tools.");
        });

        var orchestrator = CreateOrchestrator(workspace.RootPath, modelClient);
        var response = await orchestrator.ProcessAsync(
            new AgentRequest("Answer directly", workspace.RootPath));

        TestAssert.True(response.Success, "The orchestrator should succeed.");
        TestAssert.Equal("Grounded answer without tools.", response.Output);
        TestAssert.Equal(0, response.ToolResults?.Count ?? 0);
    }

    public static async Task AgentOrchestratorHandlesSingleToolCallAsync()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteText("README.md", "This project reads files safely.");

        var callCount = 0;
        var modelClient = new ScriptedModelClient(request =>
        {
            callCount++;
            return callCount switch
            {
                1 => new ModelResponse(
                    ToolCalls: new[]
                    {
                        new ModelToolCall(
                            "call-readme",
                            "read_file",
                            new Dictionary<string, string> { ["path"] = "README.md" })
                    }),
                2 => BuildFinalResponseAfterTool(
                    request,
                    "call-readme",
                    "This project reads files safely."),
                _ => throw new InvalidOperationException("Unexpected model invocation.")
            };
        });

        var orchestrator = CreateOrchestrator(workspace.RootPath, modelClient);
        var response = await orchestrator.ProcessAsync(
            new AgentRequest("Summarize the README", workspace.RootPath));

        TestAssert.True(response.Success, "The orchestrator should succeed.");
        TestAssert.Contains("This project reads files safely.", response.Output);
        TestAssert.Equal(1, response.ToolResults?.Count ?? 0);
    }

    public static async Task AgentOrchestratorHandlesMultipleToolCallsAsync()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteText("README.md", "Repository overview.");
        workspace.WriteText(
            "ProjectLens.Host.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var callCount = 0;
        var modelClient = new ScriptedModelClient(request =>
        {
            callCount++;
            return callCount switch
            {
                1 => new ModelResponse(
                    ToolCalls: new[]
                    {
                        new ModelToolCall(
                            "call-readme",
                            "read_file",
                            new Dictionary<string, string> { ["path"] = "README.md" }),
                        new ModelToolCall(
                            "call-project",
                            "read_file",
                            new Dictionary<string, string> { ["path"] = "ProjectLens.Host.csproj" })
                    }),
                2 => BuildFinalResponseAfterMultipleTools(request),
                _ => throw new InvalidOperationException("Unexpected model invocation.")
            };
        });

        var orchestrator = CreateOrchestrator(workspace.RootPath, modelClient);
        var response = await orchestrator.ProcessAsync(
            new AgentRequest("Summarize the repository", workspace.RootPath));

        TestAssert.True(response.Success, "The orchestrator should succeed.");
        TestAssert.Contains("Repository overview.", response.Output);
        TestAssert.Contains("TargetFramework", response.Output);
        TestAssert.Equal(2, response.ToolResults?.Count ?? 0);
    }

    public static async Task AgentOrchestratorStopsAtMaxIterationsAsync()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteText("README.md", "Loop forever.");

        var modelClient = new ScriptedModelClient(_ => new ModelResponse(
            ToolCalls: new[]
            {
                new ModelToolCall(
                    Guid.NewGuid().ToString("N"),
                    "read_file",
                    new Dictionary<string, string> { ["path"] = "README.md" })
            }));

        var orchestrator = CreateOrchestrator(
            workspace.RootPath,
            modelClient,
            new AgentOrchestratorOptions { MaxIterations = 2 });

        var response = await orchestrator.ProcessAsync(
            new AgentRequest("Keep trying", workspace.RootPath));

        TestAssert.False(response.Success, "The orchestrator should stop when max iterations is reached.");
        TestAssert.Contains("maximum of 2 iterations", response.ErrorMessage);
        TestAssert.Equal(2, response.ToolResults?.Count ?? 0);
    }

    private static AgentOrchestrator CreateOrchestrator(
        string workspaceRoot,
        IModelClient? modelClient = null,
        AgentOrchestratorOptions? options = null)
    {
        return new AgentOrchestrator(
            path => new ITool[]
            {
                new ListFilesTool(path),
                new ReadFileTool(path),
                new SearchFilesTool(path)
            },
            modelClient,
            options);
    }

    private static ModelResponse BuildFinalResponseAfterTool(
        ModelRequest request,
        string callId,
        string expectedSnippet)
    {
        var toolMessage = request.Conversation.OfType<ModelToolResultMessage>().Single();
        TestAssert.Equal(callId, toolMessage.CallId);
        TestAssert.Contains(expectedSnippet, toolMessage.Output);

        return new ModelResponse($"Grounded final answer: {expectedSnippet}");
    }

    private static ModelResponse BuildFinalResponseAfterMultipleTools(ModelRequest request)
    {
        var toolMessages = request.Conversation.OfType<ModelToolResultMessage>().ToArray();
        TestAssert.Equal(2, toolMessages.Length);
        TestAssert.True(
            toolMessages.Any(message => message.CallId == "call-readme" && message.Output.Contains("Repository overview.", StringComparison.Ordinal)),
            "The README tool output should be present.");
        TestAssert.True(
            toolMessages.Any(message => message.CallId == "call-project" && message.Output.Contains("TargetFramework", StringComparison.Ordinal)),
            "The project file tool output should be present.");

        return new ModelResponse("Grounded final answer: Repository overview. TargetFramework net8.0.");
    }

    private static T Deserialize<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException("Expected JSON output from the tool.");
        }

        return JsonSerializer.Deserialize<T>(json, SerializerOptions)
            ?? throw new InvalidOperationException("Failed to deserialize tool output.");
    }
}

internal sealed class ScriptedModelClient : IModelClient
{
    private readonly Func<ModelRequest, ModelResponse>[] _responses;
    private int _index;

    public ScriptedModelClient(params Func<ModelRequest, ModelResponse>[] responses)
    {
        _responses = responses;
    }

    public Task<ModelResponse> GenerateAsync(ModelRequest request, CancellationToken cancellationToken = default)
    {
        if (_responses.Length == 0)
        {
            throw new InvalidOperationException("No scripted model response is available for this call.");
        }

        var responseIndex = _responses.Length == 1
            ? 0
            : _index;

        if (responseIndex >= _responses.Length)
        {
            throw new InvalidOperationException("No scripted model response is available for this call.");
        }

        var response = _responses[responseIndex](request);
        _index++;
        return Task.FromResult(response);
    }
}

internal sealed class TestWorkspace : IDisposable
{
    public TestWorkspace()
    {
        RootPath = Path.Combine(
            Path.GetTempPath(),
            "ProjectLens.Tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(RootPath);
    }

    public string RootPath { get; }

    public void Dispose()
    {
        if (Directory.Exists(RootPath))
        {
            Directory.Delete(RootPath, true);
        }
    }

    public void WriteBinary(string relativePath, byte[] contents)
    {
        var fullPath = GetPath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, contents);
    }

    public void WriteText(string relativePath, string contents)
    {
        var fullPath = GetPath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, contents);
    }

    private string GetPath(string relativePath)
    {
        return Path.Combine(RootPath, relativePath);
    }
}

internal static class TestAssert
{
    public static void Contains(string expectedSubstring, string? actual)
    {
        if (actual is null || !actual.Contains(expectedSubstring, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Expected '{actual}' to contain '{expectedSubstring}'.");
        }
    }

    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected '{expected}', but got '{actual}'.");
        }
    }

    public static void False(bool condition, string message)
    {
        if (condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    public static void SequenceEqual<T>(IReadOnlyCollection<T> expected, IReadOnlyCollection<T> actual)
    {
        if (expected.Count != actual.Count)
        {
            throw new InvalidOperationException($"Expected {expected.Count} items, but got {actual.Count}.");
        }

        using var expectedEnumerator = expected.GetEnumerator();
        using var actualEnumerator = actual.GetEnumerator();

        while (expectedEnumerator.MoveNext() && actualEnumerator.MoveNext())
        {
            if (!EqualityComparer<T>.Default.Equals(expectedEnumerator.Current, actualEnumerator.Current))
            {
                throw new InvalidOperationException(
                    $"Expected '{expectedEnumerator.Current}', but got '{actualEnumerator.Current}'.");
            }
        }
    }

    public static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
