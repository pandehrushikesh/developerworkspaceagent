using ProjectLens.Application;
using ProjectLens.Application.Abstractions;
using ProjectLens.Domain;
using ProjectLens.Infrastructure;
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
            ("Evidence evaluator penalizes low-value paths", ToolTests.EvidenceEvaluatorPenalizesLowValuePathsAsync),
            ("Evidence evaluator detects weak exact-match evidence", ToolTests.EvidenceEvaluatorDetectsWeakExactMatchEvidenceAsync),
            ("Evidence evaluator expands feature intent terms in a bounded way", ToolTests.EvidenceEvaluatorExpandsFeatureIntentTermsInBoundedWayAsync),
            ("SearchFilesTool ranks source files above generated artifacts", ToolTests.SearchFilesToolRanksSourceFilesAboveGeneratedArtifactsAsync),
            ("SearchFilesTool prefers feature-related files over generic setup for feature tracing", ToolTests.SearchFilesToolPrefersFeatureFilesOverGenericSetupAsync),
            ("InMemoryAgentSessionStore saves and loads state", ToolTests.InMemoryAgentSessionStoreSavesAndLoadsStateAsync),
            ("FileBasedAgentSessionStore saves and reloads state across instances", ToolTests.FileBasedAgentSessionStoreSavesAndReloadsStateAcrossInstancesAsync),
            ("FileBasedAgentSessionStore returns null for missing sessions", ToolTests.FileBasedAgentSessionStoreReturnsNullForMissingSessionAsync),
            ("FileBasedAgentSessionStore persists updates for existing sessions", ToolTests.FileBasedAgentSessionStorePersistsLatestValuesAsync),
            ("FileBasedAgentSessionStore tolerates repeated quick saves for the same session", ToolTests.FileBasedAgentSessionStoreToleratesRepeatedQuickSavesForSameSessionAsync),
            ("FileBasedAgentSessionStore round-trips path-like session ids", ToolTests.FileBasedAgentSessionStoreRoundTripsPathLikeSessionIdsAsync),
            ("RuleBasedFileCompressor preserves actionable structure", ToolTests.RuleBasedFileCompressorPreservesActionableStructureAsync),
            ("RuleBasedSessionSummarizer retains actionable findings", ToolTests.RuleBasedSessionSummarizerRetainsActionableFindingsAsync),
            ("RuleBasedSessionSummarizer excludes noisy artifact-heavy evidence", ToolTests.RuleBasedSessionSummarizerExcludesNoisyArtifactHeavyEvidenceAsync),
            ("RuleBasedSessionSummarizer retains multi-file aggregation context", ToolTests.RuleBasedSessionSummarizerRetainsMultiFileAggregationContextAsync),
            ("RuleBasedSessionSummarizer keeps provisional feature flow uncertainty", ToolTests.RuleBasedSessionSummarizerKeepsProvisionalFeatureFlowUncertaintyAsync),
            ("RuleBasedSessionSummarizer promotes strong feature evidence to main-flow context", ToolTests.RuleBasedSessionSummarizerPromotesStrongFeatureEvidenceToMainFlowContextAsync),
            ("AgentOrchestrator summarizes README and project file", ToolTests.AgentOrchestratorSummarizesWorkspaceAsync),
            ("AgentOrchestrator handles missing optional files", ToolTests.AgentOrchestratorHandlesMissingWorkspaceFilesAsync),
            ("AgentOrchestrator requires registered tools", ToolTests.AgentOrchestratorRequiresRegisteredToolsAsync),
            ("AgentOrchestrator returns final answer without tool call", ToolTests.AgentOrchestratorReturnsFinalAnswerWithoutToolCallAsync),
            ("AgentOrchestrator chains previous response id after a tool call", ToolTests.AgentOrchestratorChainsPreviousResponseIdAsync),
            ("AgentOrchestrator reuses session state across follow-up prompts", ToolTests.AgentOrchestratorReusesSessionStateAcrossFollowUpPromptsAsync),
            ("AgentOrchestrator uses session context for refactor follow-up", ToolTests.AgentOrchestratorUsesSessionContextForRefactorFollowUpAsync),
            ("AgentOrchestrator preserves uncertainty for provisional feature follow-up prompts", ToolTests.AgentOrchestratorPreservesUncertaintyForProvisionalFeatureFollowUpPromptsAsync),
            ("AgentOrchestrator persists session state without summarizer", ToolTests.AgentOrchestratorPersistsSessionStateWithoutSummarizerAsync),
            ("AgentOrchestrator refreshes visited file recency", ToolTests.AgentOrchestratorRefreshesVisitedFileRecencyAsync),
            ("AgentOrchestrator curates low-value search evidence from session memory", ToolTests.AgentOrchestratorCuratesLowValueSearchEvidenceFromSessionMemoryAsync),
            ("AgentOrchestrator recovers when exact keyword search evidence is weak", ToolTests.AgentOrchestratorRecoversWhenExactKeywordSearchEvidenceIsWeakAsync),
            ("AgentOrchestrator aggregates evidence across multiple relevant files", ToolTests.AgentOrchestratorAggregatesEvidenceAcrossMultipleRelevantFilesAsync),
            ("AgentOrchestrator aggregates controller service and model evidence for feature tracing", ToolTests.AgentOrchestratorAggregatesFeatureTracingEvidenceAcrossRelevantFilesAsync),
            ("AgentOrchestrator prevents duplicate tool calls", ToolTests.AgentOrchestratorPreventsDuplicateToolCallsAsync),
            ("AgentOrchestrator continues after duplicate search prevention", ToolTests.AgentOrchestratorContinuesAfterDuplicateSearchPreventionAsync),
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

    public static Task EvidenceEvaluatorPenalizesLowValuePathsAsync()
    {
        IEvidenceQualityEvaluator evaluator = new RuleBasedEvidenceQualityEvaluator();

        TestAssert.True(evaluator.IsLowValuePath("obj/Debug/net8.0/Generated.g.cs"), "obj paths should be treated as low-value.");
        TestAssert.False(evaluator.IsLowValuePath("src/AgentOrchestrator.cs"), "source files should not be treated as low-value.");

        var generatedScore = evaluator.ScoreFile(
            "obj/Debug/net8.0/Generated.g.cs",
            "public class GeneratedArtifacts {}",
            "Explain the agent orchestrator");
        var sourceScore = evaluator.ScoreFile(
            "src/AgentOrchestrator.cs",
            "public sealed class AgentOrchestrator {}",
            "Explain the agent orchestrator");

        TestAssert.True(sourceScore > generatedScore, "Source files should score above generated artifacts.");
        return Task.CompletedTask;
    }

    public static Task EvidenceEvaluatorDetectsWeakExactMatchEvidenceAsync()
    {
        IEvidenceQualityEvaluator evaluator = new RuleBasedEvidenceQualityEvaluator();
        var assessment = evaluator.AssessSearchEvidence(
            [
                new EvidenceMatch(
                    "obj/Debug/net8.0/ProjectLens.AssemblyInfo.cs",
                    "internal static class AssemblyInfoMarker {}"),
                new EvidenceMatch(
                    "ProjectLens.Host/appsettings.json",
                    "\"unzip\": true")
            ],
            "Search for unzip a file related logic and explain the flow",
            5);

        TestAssert.True(assessment.IsWeakEvidence, "Low-value and non-source exact matches should be treated as weak evidence.");
        TestAssert.False(assessment.HasMeaningfulSourceMatch, "Weak exact-match evidence should not report meaningful source coverage.");
        TestAssert.Contains("Weak evidence:", assessment.RecoveryGuidance);
        TestAssert.Contains("extract", assessment.RecoveryGuidance);
        TestAssert.Contains("inspect a likely main source file", assessment.RecoveryGuidance);
        return Task.CompletedTask;
    }

    public static Task EvidenceEvaluatorExpandsFeatureIntentTermsInBoundedWayAsync()
    {
        IEvidenceQualityEvaluator evaluator = new RuleBasedEvidenceQualityEvaluator();
        var expandedTerms = evaluator.ExpandIntentTerms("Trace how blog creation works across the codebase");

        TestAssert.True(evaluator.IsFeatureTracingPrompt("Trace how blog creation works across the codebase"), "The prompt should be recognized as feature tracing.");
        TestAssert.Contains("blog", string.Join(", ", expandedTerms));
        TestAssert.Contains("post", string.Join(", ", expandedTerms));
        TestAssert.Contains("create", string.Join(", ", expandedTerms));
        TestAssert.Contains("publish", string.Join(", ", expandedTerms));
        TestAssert.True(expandedTerms.Count <= 10, "Feature-term expansion should stay bounded.");
        return Task.CompletedTask;
    }

    public static async Task SearchFilesToolRanksSourceFilesAboveGeneratedArtifactsAsync()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteText(
            Path.Combine("obj", "Debug", "net8.0", "ProjectLens.AssemblyInfo.cs"),
            "internal static class AssemblyInfoMarker { } // agent orchestrator");
        workspace.WriteText(
            Path.Combine("src", "AgentOrchestrator.cs"),
            "public sealed class AgentOrchestrator { } // agent orchestrator");

        var tool = new SearchFilesTool(workspace.RootPath, new RuleBasedEvidenceQualityEvaluator());
        var result = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["query"] = "agent orchestrator",
            ["maxResults"] = "1"
        });

        TestAssert.True(result.Success, "The tool should succeed.");
        var response = Deserialize<SearchFilesResponse>(result.Output);
        var match = response.Matches.Single();

        TestAssert.Equal("src/AgentOrchestrator.cs", match.Path);
    }

    public static async Task SearchFilesToolPrefersFeatureFilesOverGenericSetupAsync()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteText(
            Path.Combine("MyBlog.Api", "Program.cs"),
            """
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddAuthentication();
            builder.Services.AddControllers();
            """);
        workspace.WriteText(
            Path.Combine("MyBlog.Api", "Controllers", "BlogsController.cs"),
            """
            public sealed class BlogsController
            {
                public async Task<IActionResult> CreateBlog(CreateBlogRequest request)
                {
                    return Ok();
                }
            }
            """);
        workspace.WriteText(
            Path.Combine("src", "App.jsx"),
            """
            async function handlePublish() {
              await apiRequest('/api/blogs', { method: 'POST' })
            }
            """);

        var tool = new SearchFilesTool(workspace.RootPath, new RuleBasedEvidenceQualityEvaluator());
        var result = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["query"] = "blog",
            ["maxResults"] = "3"
        });

        TestAssert.True(result.Success, "The tool should succeed.");
        var response = Deserialize<SearchFilesResponse>(result.Output);

        TestAssert.Equal("MyBlog.Api/Controllers/BlogsController.cs", response.Matches.First().Path);
        TestAssert.False(
            string.Equals(response.Matches.First().Path, "MyBlog.Api/Program.cs", StringComparison.Ordinal),
            "Feature tracing should not default to Program.cs.");
    }

    public static async Task InMemoryAgentSessionStoreSavesAndLoadsStateAsync()
    {
        IAgentSessionStore store = new InMemoryAgentSessionStore();
        var sessionState = new AgentSessionState
        {
            SessionId = "session-1",
            WorkspacePath = "workspace",
            WorkingSummary = "Summary",
            VisitedFiles = ["README.md"],
            RecentToolHistory = ["read_file: README.md"]
        };

        await store.SaveAsync(sessionState);
        var loadedState = await store.GetAsync("session-1");

        TestAssert.NotNull(loadedState);
        TestAssert.Equal("Summary", loadedState!.WorkingSummary);
        TestAssert.SequenceEqual(new[] { "README.md" }, loadedState.VisitedFiles.ToArray());
        TestAssert.True(loadedState.CreatedAtUtc != default, "The created timestamp should be populated.");
        TestAssert.True(loadedState.UpdatedAtUtc != default, "The updated timestamp should be populated.");
    }

    public static async Task FileBasedAgentSessionStoreSavesAndReloadsStateAcrossInstancesAsync()
    {
        using var workspace = new TestWorkspace();
        var firstStore = new FileBasedAgentSessionStore(workspace.RootPath);
        var sessionState = new AgentSessionState
        {
            SessionId = "session-1",
            WorkspacePath = "workspace",
            WorkingSummary = "Summary",
            VisitedFiles = ["README.md"],
            RecentToolHistory = ["read_file: README.md"]
        };

        await firstStore.SaveAsync(sessionState);

        var secondStore = new FileBasedAgentSessionStore(workspace.RootPath);
        var loadedState = await secondStore.GetAsync("session-1");

        TestAssert.NotNull(loadedState);
        TestAssert.Equal("Summary", loadedState!.WorkingSummary);
        TestAssert.SequenceEqual(new[] { "README.md" }, loadedState.VisitedFiles.ToArray());
        TestAssert.True(loadedState.CreatedAtUtc != default, "The created timestamp should be preserved.");
        TestAssert.True(loadedState.UpdatedAtUtc != default, "The updated timestamp should be preserved.");
        TestAssert.True(
            loadedState.UpdatedAtUtc >= loadedState.CreatedAtUtc,
            "The updated timestamp should not be earlier than creation.");
    }

    public static async Task FileBasedAgentSessionStoreReturnsNullForMissingSessionAsync()
    {
        using var workspace = new TestWorkspace();
        IAgentSessionStore store = new FileBasedAgentSessionStore(workspace.RootPath);

        var loadedState = await store.GetAsync("missing-session");

        TestAssert.Null(loadedState);
    }

    public static async Task FileBasedAgentSessionStorePersistsLatestValuesAsync()
    {
        using var workspace = new TestWorkspace();
        IAgentSessionStore firstStore = new FileBasedAgentSessionStore(workspace.RootPath);
        var initialState = new AgentSessionState
        {
            SessionId = "session-1",
            WorkspacePath = "workspace",
            WorkingSummary = "Initial summary",
            VisitedFiles = ["README.md"],
            RecentToolHistory = ["read_file: README.md"]
        };

        await firstStore.SaveAsync(initialState);
        var savedInitialState = await firstStore.GetAsync("session-1");

        TestAssert.NotNull(savedInitialState);

        await Task.Delay(20);

        IAgentSessionStore secondStore = new FileBasedAgentSessionStore(workspace.RootPath);
        await secondStore.SaveAsync(savedInitialState! with
        {
            WorkingSummary = "Updated summary",
            VisitedFiles = ["README.md", "src/AgentOrchestrator.cs"],
            RecentToolHistory = ["search_files: AgentOrchestrator"]
        });

        var reloadedState = await new FileBasedAgentSessionStore(workspace.RootPath).GetAsync("session-1");

        TestAssert.NotNull(reloadedState);
        TestAssert.Equal("Updated summary", reloadedState!.WorkingSummary);
        TestAssert.SequenceEqual(
            new[] { "README.md", "src/AgentOrchestrator.cs" },
            reloadedState.VisitedFiles.ToArray());
        TestAssert.Equal(savedInitialState.CreatedAtUtc, reloadedState.CreatedAtUtc);
        TestAssert.True(
            reloadedState.UpdatedAtUtc > savedInitialState.UpdatedAtUtc,
            "The updated timestamp should advance on resave.");
    }

    public static async Task FileBasedAgentSessionStoreToleratesRepeatedQuickSavesForSameSessionAsync()
    {
        using var workspace = new TestWorkspace();
        IAgentSessionStore store = new FileBasedAgentSessionStore(workspace.RootPath);

        var saveTasks = Enumerable.Range(0, 8)
            .Select(index => store.SaveAsync(new AgentSessionState
            {
                SessionId = "session-quick-save",
                WorkspacePath = "workspace",
                WorkingSummary = $"Summary {index}",
                VisitedFiles = [$"file-{index}.cs"],
                RecentToolHistory = [$"read_file: file-{index}.cs"]
            }))
            .ToArray();

        await Task.WhenAll(saveTasks);

        var loadedState = await new FileBasedAgentSessionStore(workspace.RootPath).GetAsync("session-quick-save");

        TestAssert.NotNull(loadedState);
        TestAssert.True(loadedState!.CreatedAtUtc != default, "CreatedAtUtc should still be populated.");
        TestAssert.True(loadedState.UpdatedAtUtc != default, "UpdatedAtUtc should still be populated.");
        TestAssert.True(
            Enumerable.Range(0, 8).Select(index => $"Summary {index}").Contains(loadedState.WorkingSummary, StringComparer.Ordinal),
            "One of the repeated saves should persist without throwing.");
        TestAssert.Equal(1, Directory.GetFiles(Path.Combine(workspace.RootPath, ".sessions"), "*.json").Length);
    }

    public static async Task FileBasedAgentSessionStoreRoundTripsPathLikeSessionIdsAsync()
    {
        using var workspace = new TestWorkspace();
        var sessionId = @"C:\repo\feature/branch:session?name=*demo*";
        IAgentSessionStore store = new FileBasedAgentSessionStore(workspace.RootPath);
        var sessionState = new AgentSessionState
        {
            SessionId = sessionId,
            WorkspacePath = "workspace",
            WorkingSummary = "Path-like id summary",
            VisitedFiles = ["README.md"],
            RecentToolHistory = ["read_file: README.md"]
        };

        await store.SaveAsync(sessionState);
        var loadedState = await new FileBasedAgentSessionStore(workspace.RootPath).GetAsync(sessionId);
        var sessionFiles = Directory.GetFiles(Path.Combine(workspace.RootPath, ".sessions"), "*.json");

        TestAssert.NotNull(loadedState);
        TestAssert.Equal(sessionId, loadedState!.SessionId);
        TestAssert.Equal(1, sessionFiles.Length);
        TestAssert.False(
            Path.GetFileName(sessionFiles[0]).Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal),
            "The persisted file name should be safe.");
        TestAssert.False(
            Path.GetFileName(sessionFiles[0]).Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal),
            "The persisted file name should be safe.");
    }

    public static Task RuleBasedFileCompressorPreservesActionableStructureAsync()
    {
        IFileCompressor compressor = new RuleBasedFileCompressor();
        var compressed = compressor.Compress(
            "src/AgentOrchestrator.cs",
            """
            namespace ProjectLens.Application;

            public sealed class AgentOrchestrator
            {
                public Task ProcessAsync() => Task.CompletedTask;

                public async Task RefactorAsync()
                {
                    if (true)
                    {
                        await ProcessAsync();
                    }

                    return;
                }
            }
            """,
            "Now refactor that logic.");

        TestAssert.Contains("File: src/AgentOrchestrator.cs", compressed);
        TestAssert.Contains("Preview:", compressed);
        TestAssert.Contains("Likely classes:", compressed);
        TestAssert.Contains("Likely methods:", compressed);
        TestAssert.Contains("ProcessAsync", compressed);
        TestAssert.Contains("RefactorAsync", compressed);
        TestAssert.Contains("Control flow:", compressed);
        TestAssert.Contains("await ProcessAsync();", compressed);
        TestAssert.Contains("Relevant snippets:", compressed);
        return Task.CompletedTask;
    }

    public static Task RuleBasedSessionSummarizerRetainsActionableFindingsAsync()
    {
        ISessionSummarizer summarizer = new RuleBasedSessionSummarizer(new RuleBasedEvidenceQualityEvaluator());
        var sessionState = new AgentSessionState
        {
            SessionId = "session-1",
            WorkspacePath = "workspace",
            WorkingSummary = "Earlier summary.",
            VisitedFiles = ["README.md", "src/AgentOrchestrator.cs"],
            RecentToolHistory = ["search_files: AgentOrchestrator.cs"]
        };

        var summary = summarizer.UpdateSummary(
            sessionState,
            "read_file",
            """
            File: src/Installer.cs
            Preview: public sealed class Installer { public async Task InstallAsync() { ... } }
            Likely classes: Installer
            Likely methods: DiscoverArchives, EstimateArchiveSize, ValidateDiskSpace, ExtractArchives, ConfirmInstallation
            Control flow: if (!await ValidateDiskSpace()), await ExtractArchives(), return;
            Relevant snippets:
            - await DiscoverArchives();
            - await EstimateArchiveSize();
            - await ExtractArchives();
            Evidence basis: read_file returned truncated content at 8000 characters; recommendations should stay grounded to that partial excerpt.
            """);

        TestAssert.Contains("Earlier summary.", summary);
        TestAssert.Contains("Likely main flow files: src/Installer.cs", summary);
        TestAssert.Contains("Important symbols: Installer", summary);
        TestAssert.Contains("EstimateArchiveSize", summary);
        TestAssert.Contains("ValidateDiskSpace", summary);
        TestAssert.Contains("Evidence limitations:", summary);
        TestAssert.Contains("truncated content", summary);
        TestAssert.Contains("partial excerpt", summary);
        return Task.CompletedTask;
    }

    public static Task RuleBasedSessionSummarizerExcludesNoisyArtifactHeavyEvidenceAsync()
    {
        ISessionSummarizer summarizer = new RuleBasedSessionSummarizer(new RuleBasedEvidenceQualityEvaluator());
        var sessionState = new AgentSessionState
        {
            SessionId = "session-1",
            WorkspacePath = "workspace",
            WorkingSummary = "Earlier summary.",
            VisitedFiles = ["src/AgentOrchestrator.cs", "obj/Debug/net8.0/ProjectLens.AssemblyInfo.cs"],
            RecentToolHistory = ["search_files: agent orchestrator"]
        };

        var summary = summarizer.UpdateSummary(
            sessionState,
            "search_files",
            """
            search_files query: agent orchestrator
            Total matches: 2
            Evidence basis: search_files returns filename matches and snippets only; file contents have not been fully read yet.
            - obj/Debug/net8.0/ProjectLens.AssemblyInfo.cs: agent orchestrator generated metadata
            - src/AgentOrchestrator.cs: public sealed class AgentOrchestrator
            """);

        TestAssert.Contains("Likely main flow files: src/AgentOrchestrator.cs", summary);
        TestAssert.False(
            summary.Contains("Likely main flow files: obj/Debug/net8.0/ProjectLens.AssemblyInfo.cs", StringComparison.Ordinal),
            "Low-value artifacts should not dominate the summary.");
        TestAssert.False(
            summary.Contains("Visited files: src/AgentOrchestrator.cs, obj/Debug/net8.0/ProjectLens.AssemblyInfo.cs", StringComparison.Ordinal),
            "Low-value visited files should be filtered when higher-quality evidence exists.");
        TestAssert.Contains("Evidence limitations:", summary);
        return Task.CompletedTask;
    }

    public static Task RuleBasedSessionSummarizerRetainsMultiFileAggregationContextAsync()
    {
        ISessionSummarizer summarizer = new RuleBasedSessionSummarizer(new RuleBasedEvidenceQualityEvaluator());
        var sessionState = new AgentSessionState
        {
            SessionId = "session-aggregation",
            WorkspacePath = "workspace",
            WorkingSummary = "Earlier summary.",
            VisitedFiles = ["src/BlogController.cs", "src/PostService.cs"],
            RecentToolHistory = ["read_file: BlogController", "read_file: PostService"]
        };

        var summary = summarizer.UpdateSummary(
            sessionState,
            "read_file",
            """
            File: src/BlogController.cs
            Preview: public sealed class BlogController { public Task ShowPostAsync() { ... } }
            Evidence basis: read_file returned a bounded excerpt of 420 characters; use observed facts from this excerpt and label broader refactor ideas as inferred.
            Aggregation context:
            Likely main flow file: src/BlogController.cs
            Selected file: src/BlogController.cs | reason: Likely main flow file based on the strongest meaningful source match for the current request.
            Supporting file: src/PostService.cs | reason: Supporting source file selected as additional relevant evidence for the current request.
            Observed file summary: src/BlogController.cs => BlogController delegates post loading to PostService and returns a page model.
            Observed file summary: src/PostService.cs => PostService coordinates repository calls and maps the result for the controller.
            Aggregation limitation: Multi-file aggregation currently covers 2 of 2 selected file(s).
            """);

        TestAssert.Contains("Likely main flow files: src/BlogController.cs", summary);
        TestAssert.Contains("Supporting files: src/PostService.cs", summary);
        TestAssert.Contains("Observed file summary: src/BlogController.cs", summary);
        TestAssert.Contains("Observed file summary: src/PostService.cs", summary);
        TestAssert.Contains("Multi-file aggregation currently covers 2 of 2 selected file(s).", summary);
        return Task.CompletedTask;
    }

    public static Task RuleBasedSessionSummarizerKeepsProvisionalFeatureFlowUncertaintyAsync()
    {
        ISessionSummarizer summarizer = new RuleBasedSessionSummarizer(new RuleBasedEvidenceQualityEvaluator());
        var sessionState = new AgentSessionState
        {
            SessionId = "session-feature-provisional",
            WorkspacePath = "workspace",
            WorkingSummary = "Earlier summary.",
            VisitedFiles = ["MyBlog.Api/Controllers/BlogsController.cs"],
            RecentToolHistory = ["search_files: blog create"]
        };

        var summary = summarizer.UpdateSummary(
            sessionState,
            "search_files",
            """
            search_files query: blog
            Total matches: 4
            Evidence basis: search_files returns filename matches and snippets only; file contents have not been fully read yet.
            Aggregation context:
            Feature flow confidence: provisional
            Likely main flow file: MyBlog.Api/Controllers/BlogsController.cs
            Selected file: MyBlog.Api/Controllers/BlogsController.cs | reason: Likely main flow file based on the strongest meaningful source match for the current request.
            Supporting file: src/App.jsx | reason: Supporting source file selected as additional relevant evidence for the current request.
            Supporting file: MyBlog.Api/Models/BlogModels.cs | reason: Supporting source file selected as additional relevant evidence for the current request.
            Aggregation limitation: Selection is based on ranked search matches and snippets; 0 of 3 selected file(s) have been read so far.
            Aggregation limitation: Feature flow is still being traced; the current main-flow file is provisional until more supporting files are read.
            """);

        TestAssert.Contains("Feature flow candidates: MyBlog.Api/Controllers/BlogsController.cs", summary);
        TestAssert.Contains("Current feature-flow understanding is provisional", summary);
        TestAssert.Contains("Feature flow is still being traced", summary);
        return Task.CompletedTask;
    }

    public static Task RuleBasedSessionSummarizerPromotesStrongFeatureEvidenceToMainFlowContextAsync()
    {
        ISessionSummarizer summarizer = new RuleBasedSessionSummarizer(new RuleBasedEvidenceQualityEvaluator());
        var sessionState = new AgentSessionState
        {
            SessionId = "session-feature-strong",
            WorkspacePath = "workspace",
            WorkingSummary = "Earlier summary.",
            VisitedFiles = ["MyBlog.Api/Controllers/BlogsController.cs", "src/App.jsx"],
            RecentToolHistory = ["read_file: BlogsController", "read_file: App.jsx"]
        };

        var summary = summarizer.UpdateSummary(
            sessionState,
            "read_file",
            """
            File: src/App.jsx
            Evidence basis: read_file returned a bounded excerpt of 320 characters; use observed facts from this excerpt and label broader refactor ideas as inferred.
            Aggregation context:
            Feature flow confidence: strong
            Likely main flow file: MyBlog.Api/Controllers/BlogsController.cs
            Selected file: MyBlog.Api/Controllers/BlogsController.cs | reason: Likely main flow file based on the strongest meaningful source match for the current request.
            Supporting file: src/App.jsx | reason: Supporting source file selected as additional relevant evidence for the current request.
            Observed file summary: MyBlog.Api/Controllers/BlogsController.cs => BlogsController handles blog creation requests.
            Observed file summary: src/App.jsx => App.jsx submits the publish call to the API.
            Aggregation limitation: Multi-file aggregation currently covers 2 of 2 selected file(s).
            """);

        TestAssert.Contains("Likely main flow files: MyBlog.Api/Controllers/BlogsController.cs", summary);
        TestAssert.False(
            summary.Contains("Current feature-flow understanding is provisional", StringComparison.Ordinal),
            "Strong feature evidence should be allowed to become main-flow context.");
        return Task.CompletedTask;
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
            TestAssert.Null(request.PreviousResponseId);
            return new ModelResponse("Grounded answer without tools.");
        });

        var orchestrator = CreateOrchestrator(workspace.RootPath, modelClient);
        var response = await orchestrator.ProcessAsync(
            new AgentRequest("Answer directly", workspace.RootPath));

        TestAssert.True(response.Success, "The orchestrator should succeed.");
        TestAssert.Equal("Grounded answer without tools.", response.Output);
        TestAssert.Equal(0, response.ToolResults?.Count ?? 0);
    }

    public static async Task AgentOrchestratorChainsPreviousResponseIdAsync()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteText("README.md", "This project reads files safely.");

        var callCount = 0;
        var modelClient = new ScriptedModelClient(request =>
        {
            callCount++;

            if (callCount == 1)
            {
                TestAssert.Null(request.PreviousResponseId);
                TestAssert.Equal(1, request.Conversation.Count);

                return new ModelResponse(
                    ToolCalls: new[]
                    {
                        new ModelToolCall(
                            "call-readme",
                            "read_file",
                            new Dictionary<string, string> { ["path"] = "README.md" })
                    },
                    ResponseId: "resp-1");
            }

            if (callCount == 2)
            {
                TestAssert.Equal("resp-1", request.PreviousResponseId);
                return BuildFinalResponseAfterTool(
                    request,
                    "call-readme",
                    "This project reads files safely.",
                    responseId: "resp-2");
            }

            throw new InvalidOperationException("Unexpected model invocation.");
        });

        var orchestrator = CreateOrchestrator(workspace.RootPath, modelClient);
        var response = await orchestrator.ProcessAsync(
            new AgentRequest("Summarize the README", workspace.RootPath));

        TestAssert.True(response.Success, "The orchestrator should succeed.");
        TestAssert.Contains("This project reads files safely.", response.Output);
        TestAssert.Equal(1, response.ToolResults?.Count ?? 0);
    }

    public static async Task AgentOrchestratorReusesSessionStateAcrossFollowUpPromptsAsync()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteText("README.md", "ProjectLens remembers context.");

        var sessionStore = new InMemoryAgentSessionStore();
        IFileCompressor fileCompressor = new RuleBasedFileCompressor();
        ISessionSummarizer sessionSummarizer = new RuleBasedSessionSummarizer();

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
                    },
                    ResponseId: "resp-session-1"),
                2 => BuildFinalResponseAfterTool(
                    request,
                    "call-readme",
                    "ProjectLens remembers context.",
                    responseId: "resp-session-2"),
                3 => BuildFollowUpResponseWithSessionContext(request),
                _ => throw new InvalidOperationException("Unexpected model invocation.")
            };
        });

        var orchestrator = new AgentOrchestrator(
            path => new ITool[]
            {
                new ListFilesTool(path),
                new ReadFileTool(path),
                new SearchFilesTool(path)
            },
            modelClient,
            new AgentOrchestratorOptions { MaxIterations = 4 },
            sessionStore,
            fileCompressor,
            sessionSummarizer);

        var firstResponse = await orchestrator.ProcessAsync(
            new AgentRequest("Summarize the README", workspace.RootPath));
        var secondResponse = await orchestrator.ProcessAsync(
            new AgentRequest("What did we already inspect?", workspace.RootPath));

        TestAssert.True(firstResponse.Success, "The first response should succeed.");
        TestAssert.True(secondResponse.Success, "The follow-up response should succeed.");
        TestAssert.Contains("README.md", secondResponse.Output);
        TestAssert.Contains("session summary", secondResponse.Output);
    }

    public static async Task AgentOrchestratorUsesSessionContextForRefactorFollowUpAsync()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteText(
            "installer.cs",
            """
            public sealed class Installer
            {
                public async Task InstallAsync()
                {
                    await DiscoverArchives();
                    if (!await ValidateDiskSpace())
                    {
                        return;
                    }

                    await ExtractArchives();
                    await ConfirmInstallation();
                }
            }
            """);

        var sessionStore = new InMemoryAgentSessionStore();
        IFileCompressor fileCompressor = new RuleBasedFileCompressor();
        ISessionSummarizer sessionSummarizer = new RuleBasedSessionSummarizer();

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
                            "call-installer",
                            "read_file",
                            new Dictionary<string, string> { ["path"] = "installer.cs" })
                    },
                    ResponseId: "resp-refactor-1"),
                2 => BuildFinalResponseAfterTool(
                    request,
                    "call-installer",
                    "Installer",
                    responseId: "resp-refactor-2"),
                3 => BuildRefactorFollowUpFromSessionContext(request),
                _ => throw new InvalidOperationException("Unexpected model invocation.")
            };
        });

        var orchestrator = new AgentOrchestrator(
            path => new ITool[]
            {
                new ListFilesTool(path),
                new ReadFileTool(path),
                new SearchFilesTool(path)
            },
            modelClient,
            new AgentOrchestratorOptions { MaxIterations = 4 },
            sessionStore,
            fileCompressor,
            sessionSummarizer);

        var firstResponse = await orchestrator.ProcessAsync(
            new AgentRequest("Explain the installer flow", workspace.RootPath));
        var secondResponse = await orchestrator.ProcessAsync(
            new AgentRequest("Now refactor that logic.", workspace.RootPath));

        TestAssert.True(firstResponse.Success, "The first response should succeed.");
        TestAssert.True(secondResponse.Success, "The follow-up refactor response should succeed.");
        TestAssert.Contains("Observed facts:", secondResponse.Output);
        TestAssert.Contains("Inferred recommendations:", secondResponse.Output);
        TestAssert.Contains("refactor", secondResponse.Output);
        TestAssert.Contains("InstallAsync", secondResponse.Output);
    }

    public static async Task AgentOrchestratorPreservesUncertaintyForProvisionalFeatureFollowUpPromptsAsync()
    {
        using var workspace = new TestWorkspace();
        var sessionStore = new InMemoryAgentSessionStore();
        await sessionStore.SaveAsync(new AgentSessionState
        {
            SessionId = "session-feature-follow-up",
            WorkspacePath = workspace.RootPath,
            WorkingSummary =
                """
                Feature flow confidence: provisional
                Feature flow candidates: MyBlog.Api/Controllers/BlogsController.cs, src/App.jsx, MyBlog.Api/Models/BlogModels.cs
                Evidence limitations: Feature flow is still being traced; the current main-flow file is provisional until more supporting files are read.
                """,
            VisitedFiles = ["MyBlog.Api/Controllers/BlogsController.cs", "src/App.jsx"],
            RecentToolHistory = ["search_files: blog", "read_file: BlogsController"]
        });

        var modelClient = new ScriptedModelClient(request =>
        {
            TestAssert.Contains("The existing feature-flow context is provisional", request.Instructions);
            TestAssert.Contains("do not treat any candidate file as settled truth yet", request.Instructions);
            TestAssert.Contains("Feature flow candidates:", request.Instructions);

            return new ModelResponse(
                """
                The current feature flow is still provisional.
                The strongest current candidates are MyBlog.Api/Controllers/BlogsController.cs, src/App.jsx, and MyBlog.Api/Models/BlogModels.cs.
                A refactor suggestion should stay tentative until one more supporting file confirms the feature path.
                """,
                ResponseId: "resp-feature-follow-up");
        });

        var orchestrator = new AgentOrchestrator(
            path => new ITool[]
            {
                new ListFilesTool(path),
                new ReadFileTool(path),
                new SearchFilesTool(path, new RuleBasedEvidenceQualityEvaluator())
            },
            modelClient,
            new AgentOrchestratorOptions { MaxIterations = 3 },
            sessionStore,
            new RuleBasedFileCompressor(),
            new RuleBasedSessionSummarizer(new RuleBasedEvidenceQualityEvaluator()),
            new RuleBasedEvidenceQualityEvaluator());

        var response = await orchestrator.ProcessAsync(
            new AgentRequest(
                "Now suggest a refactor for that flow.",
                workspace.RootPath,
                new Dictionary<string, string> { ["sessionId"] = "session-feature-follow-up" }));

        TestAssert.True(response.Success, "The follow-up response should succeed.");
        TestAssert.Contains("still provisional", response.Output);
        TestAssert.Contains("strongest current candidates", response.Output);
        TestAssert.Contains("tentative", response.Output);
    }

    public static async Task AgentOrchestratorPersistsSessionStateWithoutSummarizerAsync()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteText("README.md", "ProjectLens remembers context.");

        var sessionStore = new InMemoryAgentSessionStore();
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
                            "call-read-1",
                            "read_file",
                            new Dictionary<string, string> { ["path"] = "README.md" })
                    },
                    ResponseId: "resp-no-summary-1"),
                2 => new ModelResponse(
                    "Grounded final answer: README.md was inspected.",
                    ResponseId: "resp-no-summary-2"),
                _ => throw new InvalidOperationException("Unexpected model invocation.")
            };
        });

        var orchestrator = new AgentOrchestrator(
            path => new ITool[]
            {
                new ListFilesTool(path),
                new ReadFileTool(path),
                new SearchFilesTool(path)
            },
            modelClient,
            new AgentOrchestratorOptions { MaxIterations = 3 },
            sessionStore,
            fileCompressor: null,
            sessionSummarizer: null);

        var request = new AgentRequest(
            "Inspect the README",
            workspace.RootPath,
            new Dictionary<string, string> { ["sessionId"] = "session-no-summary" });

        var response = await orchestrator.ProcessAsync(request);
        var savedState = await sessionStore.GetAsync("session-no-summary");

        TestAssert.True(response.Success, "The response should succeed.");
        TestAssert.NotNull(savedState);
        TestAssert.SequenceEqual(new[] { "README.md" }, savedState!.VisitedFiles.ToArray());
        TestAssert.True(savedState.RecentToolHistory.Count > 0, "Tool history should still be persisted.");
        TestAssert.Equal(string.Empty, savedState.WorkingSummary);
    }

    public static async Task AgentOrchestratorRefreshesVisitedFileRecencyAsync()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteText("README.md", "ProjectLens remembers context.");
        workspace.WriteText("docs.txt", "Documentation");

        var sessionStore = new InMemoryAgentSessionStore();
        var sessionState = new AgentSessionState
        {
            SessionId = "session-recency",
            WorkspacePath = workspace.RootPath,
            WorkingSummary = "Summary",
            VisitedFiles = ["README.md", "docs.txt"],
            RecentToolHistory = ["read_file: README.md", "read_file: docs.txt"]
        };
        await sessionStore.SaveAsync(sessionState);

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
                            "call-read-1",
                            "read_file",
                            new Dictionary<string, string> { ["path"] = "README.md" })
                    },
                    ResponseId: "resp-recency-1"),
                2 => new ModelResponse(
                    "Grounded final answer: README.md was revisited.",
                    ResponseId: "resp-recency-2"),
                _ => throw new InvalidOperationException("Unexpected model invocation.")
            };
        });

        var orchestrator = new AgentOrchestrator(
            path => new ITool[]
            {
                new ListFilesTool(path),
                new ReadFileTool(path),
                new SearchFilesTool(path)
            },
            modelClient,
            new AgentOrchestratorOptions { MaxIterations = 3 },
            sessionStore,
            new RuleBasedFileCompressor(),
            new RuleBasedSessionSummarizer());

        var request = new AgentRequest(
            "Revisit the README",
            workspace.RootPath,
            new Dictionary<string, string> { ["sessionId"] = "session-recency" });

        var response = await orchestrator.ProcessAsync(request);
        var savedState = await sessionStore.GetAsync("session-recency");

        TestAssert.True(response.Success, "The response should succeed.");
        TestAssert.NotNull(savedState);
        TestAssert.SequenceEqual(new[] { "docs.txt", "README.md" }, savedState!.VisitedFiles.ToArray());
    }

    public static async Task AgentOrchestratorCuratesLowValueSearchEvidenceFromSessionMemoryAsync()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteText(Path.Combine("src", "AgentOrchestrator.cs"), "public sealed class AgentOrchestrator {}");
        workspace.WriteText(Path.Combine("obj", "Debug", "net8.0", "ProjectLens.AssemblyInfo.cs"), "internal static class AssemblyInfoMarker {}");

        var evidenceQualityEvaluator = new RuleBasedEvidenceQualityEvaluator();
        var sessionStore = new InMemoryAgentSessionStore();
        var modelClient = new ScriptedModelClient(
            _ => new ModelResponse(
                ToolCalls: new[]
                {
                    new ModelToolCall(
                        "call-search-1",
                        "search_files",
                        new Dictionary<string, string>
                        {
                            ["query"] = "AgentOrchestrator",
                            ["path"] = "."
                        })
                },
                ResponseId: "resp-curate-1"),
            _ => new ModelResponse(
                "Grounded final answer: AgentOrchestrator is implemented in src/AgentOrchestrator.cs.",
                ResponseId: "resp-curate-2"));

        var orchestrator = new AgentOrchestrator(
            path => new ITool[]
            {
                new ListFilesTool(path),
                new ReadFileTool(path),
                new SearchFilesTool(path, evidenceQualityEvaluator)
            },
            modelClient,
            new AgentOrchestratorOptions { MaxIterations = 3 },
            sessionStore,
            new RuleBasedFileCompressor(),
            new RuleBasedSessionSummarizer(evidenceQualityEvaluator),
            evidenceQualityEvaluator);

        var request = new AgentRequest(
            "Explain AgentOrchestrator",
            workspace.RootPath,
            new Dictionary<string, string> { ["sessionId"] = "session-curated-search" });

        var response = await orchestrator.ProcessAsync(request);
        var savedState = await sessionStore.GetAsync("session-curated-search");

        TestAssert.True(response.Success, "The response should succeed.");
        TestAssert.NotNull(savedState);
        TestAssert.SequenceEqual(new[] { "src/AgentOrchestrator.cs" }, savedState!.VisitedFiles.ToArray());
        TestAssert.Contains("src/AgentOrchestrator.cs", savedState.WorkingSummary);
        TestAssert.False(
            savedState.WorkingSummary.Contains("obj/Debug/net8.0/ProjectLens.AssemblyInfo.cs", StringComparison.Ordinal),
            "Low-value artifact paths should not dominate the persisted summary.");
    }

    public static async Task AgentOrchestratorRecoversWhenExactKeywordSearchEvidenceIsWeakAsync()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteText(
            Path.Combine("obj", "Debug", "net8.0", "GeneratedArchiveHints.g.cs"),
            "internal static class GeneratedArchiveHints { private const string Keyword = \"unzip\"; }");
        workspace.WriteText(
            Path.Combine("src", "ArchiveExtractorService.cs"),
            """
            public sealed class ArchiveExtractorService
            {
                public void ExtractArchive(string archivePath)
                {
                    OpenArchive(archivePath);
                    UnpackEntries();
                }
            }
            """);

        var evidenceQualityEvaluator = new RuleBasedEvidenceQualityEvaluator();
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
                            "call-search-unzip",
                            "search_files",
                            new Dictionary<string, string>
                            {
                                ["query"] = "unzip",
                                ["path"] = "."
                            })
                    },
                    ResponseId: "resp-weak-1"),
                2 => BuildPrematureFinalAnswerAfterWeakSearch(request),
                3 => BuildRecoverySearchAfterWeakEvidence(request),
                4 => new ModelResponse(
                    ToolCalls: new[]
                    {
                        new ModelToolCall(
                            "call-read-extract",
                            "read_file",
                            new Dictionary<string, string>
                            {
                                ["path"] = "src/ArchiveExtractorService.cs"
                            })
                    },
                    ResponseId: "resp-weak-4"),
                5 => BuildFinalResponseAfterTool(
                    request,
                    "call-read-extract",
                    "ArchiveExtractorService",
                    responseId: "resp-weak-5"),
                _ => throw new InvalidOperationException("Unexpected model invocation.")
            };
        });

        var orchestrator = new AgentOrchestrator(
            path => new ITool[]
            {
                new ListFilesTool(path),
                new ReadFileTool(path),
                new SearchFilesTool(path, evidenceQualityEvaluator)
            },
            modelClient,
            new AgentOrchestratorOptions { MaxIterations = 6 },
            evidenceQualityEvaluator: evidenceQualityEvaluator);

        var response = await orchestrator.ProcessAsync(
            new AgentRequest("Search for unzip a file related logic and explain the flow", workspace.RootPath));

        TestAssert.True(response.Success, "The orchestrator should recover from weak exact-match evidence.");
        TestAssert.Contains("ArchiveExtractorService", response.Output);
        TestAssert.True(
            response.ExecutionSteps?.Any(step =>
                step.Description.Contains("weak search evidence", StringComparison.OrdinalIgnoreCase)) == true,
            "A recovery step should be recorded when the model tries to finalize too early.");
        TestAssert.Equal(3, response.ToolResults?.Count ?? 0);
    }

    public static async Task AgentOrchestratorAggregatesEvidenceAcrossMultipleRelevantFilesAsync()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteText(
            Path.Combine("src", "BlogController.cs"),
            """
            public sealed class BlogController
            {
                private readonly PostService _postService;

                public async Task ShowPostAsync(string slug)
                {
                    await _postService.LoadPostAsync(slug);
                }
            }
            """);
        workspace.WriteText(
            Path.Combine("src", "PostService.cs"),
            """
            public sealed class PostService
            {
                private readonly PostRepository _postRepository;

                public async Task<PostModel> LoadPostAsync(string slug)
                {
                    return await _postRepository.FindBySlugAsync(slug);
                }
            }
            """);
        workspace.WriteText(
            Path.Combine("src", "PostRepository.cs"),
            """
            public sealed class PostRepository
            {
                public Task<PostModel> FindBySlugAsync(string slug) => Task.FromResult(new PostModel(slug));
            }
            """);
        workspace.WriteText(
            Path.Combine("src", "UnusedHelper.cs"),
            """
            public static class UnusedHelper
            {
                public static string FormatPost(string slug) => slug.ToUpperInvariant();
            }
            """);

        var evidenceQualityEvaluator = new RuleBasedEvidenceQualityEvaluator();
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
                            "call-search-post",
                            "search_files",
                            new Dictionary<string, string>
                            {
                                ["query"] = "post",
                                ["path"] = "."
                            })
                    },
                    ResponseId: "resp-agg-1"),
                2 => BuildReadRequestFromAggregatedSearch(request),
                3 => new ModelResponse(
                    "Grounded final answer: BlogController handles the post flow directly.",
                    ResponseId: "resp-agg-3"),
                4 => BuildSupportingReadRequestAfterAggregationPrompt(request),
                5 => BuildAggregatedFinalResponse(request),
                _ => throw new InvalidOperationException("Unexpected model invocation.")
            };
        });

        var orchestrator = new AgentOrchestrator(
            path => new ITool[]
            {
                new ListFilesTool(path),
                new ReadFileTool(path),
                new SearchFilesTool(path, evidenceQualityEvaluator)
            },
            modelClient,
            new AgentOrchestratorOptions { MaxIterations = 6 },
            evidenceQualityEvaluator: evidenceQualityEvaluator);

        var response = await orchestrator.ProcessAsync(
            new AgentRequest("Explain the post request flow and architecture", workspace.RootPath));

        TestAssert.True(response.Success, "The orchestrator should aggregate evidence across multiple files.");
        TestAssert.Contains("Observed facts:", response.Output);
        TestAssert.Contains("BlogController", response.Output);
        TestAssert.Contains("PostService", response.Output);
        TestAssert.Contains("Supporting files:", response.Output);
        TestAssert.True(
            response.ExecutionSteps?.Any(step =>
                step.Description.Contains("multi-file evidence", StringComparison.OrdinalIgnoreCase)) == true,
            "A multi-file aggregation recovery step should be recorded.");
        TestAssert.Equal(3, response.ToolResults?.Count ?? 0);
    }

    public static async Task AgentOrchestratorAggregatesFeatureTracingEvidenceAcrossRelevantFilesAsync()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteText(
            Path.Combine("MyBlog.Api", "Program.cs"),
            """
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddAuthentication();
            builder.Services.AddControllers();
            """);
        workspace.WriteText(
            Path.Combine("MyBlog.Api", "Controllers", "BlogsController.cs"),
            """
            public sealed class BlogsController
            {
                public async Task<IActionResult> Create(CreateBlogRequest request)
                {
                    var post = new BlogPost(request.Title, request.Content);
                    return Ok(post);
                }
            }
            """);
        workspace.WriteText(
            Path.Combine("MyBlog.Api", "Models", "BlogModels.cs"),
            """
            public sealed record CreateBlogRequest(string Title, string Content);
            public sealed record BlogResponse(Guid Id, string Title, string Content);
            """);
        workspace.WriteText(
            Path.Combine("src", "App.jsx"),
            """
            async function handlePublish() {
              await apiRequest('/api/blogs', { method: 'POST', body: JSON.stringify(blogForm) })
            }
            """);

        var evidenceQualityEvaluator = new RuleBasedEvidenceQualityEvaluator();
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
                            "call-search-blog-trace",
                            "search_files",
                            new Dictionary<string, string>
                            {
                                ["query"] = "blog",
                                ["path"] = "."
                            })
                    },
                    ResponseId: "resp-feature-1"),
                2 => BuildReadRequestForFeatureTrace(request),
                3 => new ModelResponse(
                    "Grounded final answer: Program.cs appears to control blog creation.",
                    ResponseId: "resp-feature-3"),
                4 => BuildFeatureSupportingReadRequest(request),
                5 => BuildFeatureTraceFinalResponse(request),
                _ => throw new InvalidOperationException("Unexpected model invocation.")
            };
        });

        var orchestrator = new AgentOrchestrator(
            path => new ITool[]
            {
                new ListFilesTool(path),
                new ReadFileTool(path),
                new SearchFilesTool(path, evidenceQualityEvaluator)
            },
            modelClient,
            new AgentOrchestratorOptions { MaxIterations = 6 },
            evidenceQualityEvaluator: evidenceQualityEvaluator);

        var response = await orchestrator.ProcessAsync(
            new AgentRequest("Trace how blog creation works across the codebase", workspace.RootPath));

        TestAssert.True(response.Success, "Feature tracing should converge on the feature flow files.");
        TestAssert.Contains("BlogsController", response.Output);
        TestAssert.True(
            response.Output.Contains("App.jsx", StringComparison.Ordinal) ||
            response.Output.Contains("BlogModels", StringComparison.Ordinal) ||
            response.Output.Contains("frontend and model files", StringComparison.Ordinal),
            "Feature tracing should incorporate frontend or model-style supporting evidence.");
        TestAssert.False(
            response.Output.Contains("Program.cs appears to control blog creation", StringComparison.Ordinal),
            "Program.cs should not dominate the feature-tracing answer.");
        TestAssert.True(
            response.ExecutionSteps?.Any(step =>
                step.Description.Contains("multi-file evidence", StringComparison.OrdinalIgnoreCase)) == true,
            "Feature tracing should request supporting evidence before finalizing.");
    }

    public static async Task AgentOrchestratorPreventsDuplicateToolCallsAsync()
    {
        using var workspace = new TestWorkspace();
        var searchTool = new CountingTool(
            "search_files",
            "Searches files.",
            new Dictionary<string, string>
            {
                ["query"] = "Query text.",
                ["path"] = "Search path."
            },
            _ => new ToolExecutionResult("search_files", true, """{"Matches":["src/AgentOrchestrator.cs"]}"""));

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
                            "call-search-1",
                            "search_files",
                            new Dictionary<string, string>
                            {
                                ["query"] = "AgentOrchestrator",
                                ["path"] = "."
                            })
                    },
                    ResponseId: "resp-search-1"),
                2 => new ModelResponse(
                    ToolCalls: new[]
                    {
                        new ModelToolCall(
                            "call-search-2",
                            "search_files",
                            new Dictionary<string, string>
                            {
                                ["query"] = "AgentOrchestrator",
                                ["path"] = "."
                            })
                    },
                    ResponseId: "resp-search-2"),
                3 => BuildFinalResponseAfterDuplicatePrevention(request, "call-search-2"),
                _ => throw new InvalidOperationException("Unexpected model invocation.")
            };
        });

        var orchestrator = new AgentOrchestrator(
            _ => new ITool[] { searchTool },
            modelClient,
            new AgentOrchestratorOptions { MaxIterations = 4 });

        var response = await orchestrator.ProcessAsync(
            new AgentRequest("Find AgentOrchestrator", workspace.RootPath));

        TestAssert.True(response.Success, "The orchestrator should succeed after blocking the duplicate.");
        TestAssert.Equal(1, searchTool.ExecutionCount);
        TestAssert.Contains("existing evidence", response.Output);
        TestAssert.True(
            response.ExecutionSteps?.Any(step => step.Description.Contains("Prevented duplicate tool call", StringComparison.Ordinal)) == true,
            "A duplicate prevention step should be recorded.");
    }

    public static async Task AgentOrchestratorContinuesAfterDuplicateSearchPreventionAsync()
    {
        using var workspace = new TestWorkspace();
        var searchTool = new CountingTool(
            "search_files",
            "Searches files.",
            new Dictionary<string, string>
            {
                ["query"] = "Query text.",
                ["path"] = "Search path."
            },
            arguments => new ToolExecutionResult(
                "search_files",
                true,
                $$"""{"Matches":["{{arguments["path"]}}/ProjectLens.Application/AgentOrchestrator.cs"]}"""));
        var readTool = new CountingTool(
            "read_file",
            "Reads a file.",
            new Dictionary<string, string>
            {
                ["path"] = "File path."
            },
            _ => new ToolExecutionResult(
                "read_file",
                true,
                """{"Path":"ProjectLens.Application/AgentOrchestrator.cs","Content":"public sealed class AgentOrchestrator {}","IsTruncated":false,"CharacterCount":40}"""));

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
                            "call-search-1",
                            "search_files",
                            new Dictionary<string, string>
                            {
                                ["query"] = "AgentOrchestrator",
                                ["path"] = "."
                            })
                    },
                    ResponseId: "resp-dup-1"),
                2 => new ModelResponse(
                    ToolCalls: new[]
                    {
                        new ModelToolCall(
                            "call-search-2",
                            "search_files",
                            new Dictionary<string, string>
                            {
                                ["query"] = "AgentOrchestrator",
                                ["path"] = "."
                            })
                    },
                    ResponseId: "resp-dup-2"),
                3 => BuildReadRequestAfterDuplicate(request, "call-search-2"),
                4 => BuildFinalAnswerAfterRead(request, "call-read-1"),
                _ => throw new InvalidOperationException("Unexpected model invocation.")
            };
        });

        var orchestrator = new AgentOrchestrator(
            _ => new ITool[] { searchTool, readTool },
            modelClient,
            new AgentOrchestratorOptions { MaxIterations = 6 });

        var response = await orchestrator.ProcessAsync(
            new AgentRequest("Explain AgentOrchestrator", workspace.RootPath));

        TestAssert.True(response.Success, "The orchestrator should recover after duplicate prevention.");
        TestAssert.Equal(1, searchTool.ExecutionCount);
        TestAssert.Equal(1, readTool.ExecutionCount);
        TestAssert.Contains("AgentOrchestrator", response.Output);
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
                    },
                    ResponseId: "resp-readme"),
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
                    },
                    ResponseId: "resp-multi"),
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
            },
            ResponseId: "resp-loop"));

        var orchestrator = CreateOrchestrator(
            workspace.RootPath,
            modelClient,
            new AgentOrchestratorOptions { MaxIterations = 2 });

        var response = await orchestrator.ProcessAsync(
            new AgentRequest("Keep trying", workspace.RootPath));

        TestAssert.False(response.Success, "The orchestrator should stop when max iterations is reached.");
        TestAssert.Contains("maximum of 2 iterations", response.ErrorMessage);
        TestAssert.Equal(1, response.ToolResults?.Count ?? 0);
    }

    private static AgentOrchestrator CreateOrchestrator(
        string workspaceRoot,
        IModelClient? modelClient = null,
        AgentOrchestratorOptions? options = null)
    {
        var evidenceQualityEvaluator = new RuleBasedEvidenceQualityEvaluator();

        return new AgentOrchestrator(
            path => new ITool[]
            {
                new ListFilesTool(path),
                new ReadFileTool(path),
                new SearchFilesTool(path, evidenceQualityEvaluator)
            },
            modelClient,
            options,
            evidenceQualityEvaluator: evidenceQualityEvaluator);
    }

    private static ModelResponse BuildFinalResponseAfterTool(
        ModelRequest request,
        string callId,
        string expectedSnippet,
        string? responseId = null)
    {
        var toolMessage = request.Conversation
            .OfType<ModelToolResultMessage>()
            .Single(message => message.CallId == callId);
        TestAssert.Equal(callId, toolMessage.CallId);
        TestAssert.Contains(expectedSnippet, toolMessage.Output);

        return new ModelResponse($"Grounded final answer: {expectedSnippet}", ResponseId: responseId);
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

        return new ModelResponse("Grounded final answer: Repository overview. TargetFramework net8.0.", ResponseId: "resp-final");
    }

    private static ModelResponse BuildFinalResponseAfterDuplicatePrevention(ModelRequest request, string duplicateCallId)
    {
        var duplicateMessage = request.Conversation
            .OfType<ModelToolResultMessage>()
            .Single(message => message.CallId == duplicateCallId);

        TestAssert.Contains("already executed earlier", duplicateMessage.Output);
        TestAssert.Contains("provide the best grounded answer", duplicateMessage.Output);
        TestAssert.Contains("Choose a different action", duplicateMessage.Output);
        TestAssert.Contains("observed facts", duplicateMessage.Output);
        TestAssert.Contains("inferred recommendations", duplicateMessage.Output);

        return new ModelResponse(
            "Grounded final answer: use the existing evidence instead of repeating the same search.",
            ResponseId: "resp-after-duplicate");
    }

    private static ModelResponse BuildPrematureFinalAnswerAfterWeakSearch(ModelRequest request)
    {
        var toolMessage = request.Conversation
            .OfType<ModelToolResultMessage>()
            .Single(message => message.CallId == "call-search-unzip");

        TestAssert.Contains("Weak evidence:", toolMessage.Output);
        TestAssert.Contains("low-value or generated paths", toolMessage.Output);
        TestAssert.Contains("extract", toolMessage.Output);
        TestAssert.Contains("inspect a likely main source file", toolMessage.Output);

        return new ModelResponse(
            "Grounded final answer: unzip logic appears to be handled in generated files.",
            ResponseId: "resp-weak-2");
    }

    private static ModelResponse BuildRecoverySearchAfterWeakEvidence(ModelRequest request)
    {
        var recoveryMessage = request.Conversation
            .OfType<ModelTextMessage>()
            .Last();

        TestAssert.Contains("Do not finalize yet", recoveryMessage.Content);
        TestAssert.Contains("Try a broader related search", recoveryMessage.Content);

        return new ModelResponse(
            ToolCalls: new[]
            {
                new ModelToolCall(
                    "call-search-extract",
                    "search_files",
                    new Dictionary<string, string>
                    {
                        ["query"] = "extract",
                        ["path"] = "."
                    })
            },
            ResponseId: "resp-weak-3");
    }

    private static ModelResponse BuildReadRequestFromAggregatedSearch(ModelRequest request)
    {
        var searchMessage = request.Conversation
            .OfType<ModelToolResultMessage>()
            .Single(message => message.CallId == "call-search-post");
        var likelyMainFlowFile = ExtractLineValue(searchMessage.Output, "Likely main flow file: ");

        TestAssert.Contains("Aggregation context:", searchMessage.Output);
        TestAssert.True(
            string.Equals(likelyMainFlowFile, "src/BlogController.cs", StringComparison.Ordinal) ||
            string.Equals(likelyMainFlowFile, "src/PostService.cs", StringComparison.Ordinal),
            "The likely main flow file should be selected from the top application flow files.");
        var expectedSupportingFlowFile = string.Equals(likelyMainFlowFile, "src/BlogController.cs", StringComparison.Ordinal)
            ? "src/PostService.cs"
            : "src/BlogController.cs";
        TestAssert.Contains($"Supporting file: {expectedSupportingFlowFile}", searchMessage.Output);
        TestAssert.Contains("Supporting file: src/PostRepository.cs", searchMessage.Output);
        TestAssert.False(
            searchMessage.Output.Contains("src/UnusedHelper.cs", StringComparison.Ordinal),
            "Aggregation should stay bounded to the top relevant files.");

        return new ModelResponse(
            ToolCalls: new[]
            {
                new ModelToolCall(
                    "call-read-controller",
                    "read_file",
                    new Dictionary<string, string>
                    {
                        ["path"] = likelyMainFlowFile
                    })
            },
            ResponseId: "resp-agg-2");
    }

    private static ModelResponse BuildSupportingReadRequestAfterAggregationPrompt(ModelRequest request)
    {
        var recoveryPrompt = request.Conversation
            .OfType<ModelTextMessage>()
            .Last();

        TestAssert.Contains("Multiple meaningful source files appear relevant.", recoveryPrompt.Content);
        TestAssert.Contains("supporting file", recoveryPrompt.Content);

        return new ModelResponse(
            ToolCalls: new[]
            {
                new ModelToolCall(
                    "call-read-support",
                    "read_file",
                    new Dictionary<string, string>
                    {
                        ["path"] = ExtractFirstSuggestedPath(recoveryPrompt.Content)
                    })
            },
            ResponseId: "resp-agg-4");
    }

    private static ModelResponse BuildAggregatedFinalResponse(ModelRequest request)
    {
        var toolMessages = request.Conversation.OfType<ModelToolResultMessage>().ToArray();
        var controllerMessage = toolMessages.Single(message => message.CallId == "call-read-controller");
        var supportingMessage = toolMessages.Single(message => message.CallId == "call-read-support");

        TestAssert.Contains("Observed file summary: src/BlogController.cs", supportingMessage.Output);
        TestAssert.Contains("Observed file summary: src/PostService.cs", supportingMessage.Output);
        TestAssert.Contains("Aggregation limitation: Multi-file aggregation currently covers 2 of 3 selected file(s).", supportingMessage.Output);
        TestAssert.True(
            controllerMessage.Output.Contains("BlogController", StringComparison.Ordinal) ||
            controllerMessage.Output.Contains("PostService", StringComparison.Ordinal),
            "The first read should contribute one of the primary flow files.");
        TestAssert.Contains("PostService", supportingMessage.Output);

        return new ModelResponse(
            """
            Observed facts:
            - BlogController and PostService jointly define the post request flow.
            - Supporting files: PostService coordinates the repository call, and PostRepository appears to supply the persisted post data.

            Inferred recommendations:
            - Keep the controller thin and treat PostService as the orchestration layer for the request flow.
            """,
            ResponseId: "resp-agg-5");
    }

    private static string ExtractLineValue(string content, string prefix)
    {
        return content
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .First(line => line.StartsWith(prefix, StringComparison.Ordinal))[prefix.Length..]
            .Trim();
    }

    private static ModelResponse BuildReadRequestForFeatureTrace(ModelRequest request)
    {
        var searchMessage = request.Conversation
            .OfType<ModelToolResultMessage>()
            .Single(message => message.CallId == "call-search-blog-trace");

        TestAssert.Contains("Likely main flow file: MyBlog.Api/Controllers/BlogsController.cs", searchMessage.Output);
        TestAssert.Contains("Supporting file: src/App.jsx", searchMessage.Output);
        TestAssert.Contains("Supporting file: MyBlog.Api/Models/BlogModels.cs", searchMessage.Output);
        TestAssert.False(
            searchMessage.Output.Contains("Likely main flow file: MyBlog.Api/Program.cs", StringComparison.Ordinal),
            "Feature tracing should not anchor on Program.cs.");

        return new ModelResponse(
            ToolCalls: new[]
            {
                new ModelToolCall(
                    "call-read-blog-controller",
                    "read_file",
                    new Dictionary<string, string>
                    {
                        ["path"] = "MyBlog.Api/Controllers/BlogsController.cs"
                    })
            },
            ResponseId: "resp-feature-2");
    }

    private static ModelResponse BuildFeatureSupportingReadRequest(ModelRequest request)
    {
        var recoveryPrompt = request.Conversation
            .OfType<ModelTextMessage>()
            .Last();

        TestAssert.Contains("For feature tracing, prioritize controller/service/model/frontend files", recoveryPrompt.Content);
        var supportingPath = ExtractFirstSuggestedPath(recoveryPrompt.Content);

        return new ModelResponse(
            ToolCalls: new[]
            {
                new ModelToolCall(
                    "call-read-blog-ui",
                    "read_file",
                    new Dictionary<string, string>
                    {
                        ["path"] = supportingPath
                    })
            },
            ResponseId: "resp-feature-4");
    }

    private static ModelResponse BuildFeatureTraceFinalResponse(ModelRequest request)
    {
        var toolMessages = request.Conversation.OfType<ModelToolResultMessage>().ToArray();
        var controllerMessage = toolMessages.Single(message => message.CallId == "call-read-blog-controller");
        var uiMessage = toolMessages.Single(message => message.CallId == "call-read-blog-ui");
        var supportedUiModel = uiMessage.Output.Contains("Observed file summary: src/App.jsx", StringComparison.Ordinal)
            ? "src/App.jsx"
            : "MyBlog.Api/Models/BlogModels.cs";

        TestAssert.Contains("BlogsController", controllerMessage.Output);
        TestAssert.Contains("Observed file summary: MyBlog.Api/Controllers/BlogsController.cs", uiMessage.Output);
        TestAssert.Contains($"Observed file summary: {supportedUiModel}", uiMessage.Output);
        TestAssert.Contains("Feature flow is still being traced; the current main-flow file is provisional", controllerMessage.Output);

        return new ModelResponse(
            """
            Observed facts:
            - BlogsController appears to own the API-side blog creation endpoint and constructs the new blog response.
            - Supporting files include the frontend publish flow and request/response models for the feature.

            Inferred recommendations:
            - Treat BlogsController as the current main feature-flow file, with frontend and model files as supporting evidence.
            """,
            ResponseId: "resp-feature-5");
    }

    private static string ExtractFirstSuggestedPath(string recoveryPrompt)
    {
        const string prefix = "preferably from: ";
        var startIndex = recoveryPrompt.IndexOf(prefix, StringComparison.Ordinal);
        if (startIndex < 0)
        {
            throw new InvalidOperationException("Expected a preferred supporting-file hint in the recovery prompt.");
        }

        var value = recoveryPrompt[(startIndex + prefix.Length)..].Trim();
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
    }

    private static ModelResponse BuildReadRequestAfterDuplicate(ModelRequest request, string duplicateCallId)
    {
        var toolMessages = request.Conversation.OfType<ModelToolResultMessage>().ToArray();
        TestAssert.True(toolMessages.Any(message =>
                message.CallId == duplicateCallId &&
                message.Output.Contains("already executed earlier", StringComparison.Ordinal) &&
                message.Output.Contains("Prefer read_file", StringComparison.Ordinal) &&
                message.Output.Contains("observed facts", StringComparison.Ordinal)),
            "The duplicate prevention message should be present.");

        return new ModelResponse(
            ToolCalls: new[]
            {
                new ModelToolCall(
                    "call-read-1",
                    "read_file",
                    new Dictionary<string, string>
                    {
                        ["path"] = "ProjectLens.Application/AgentOrchestrator.cs"
                    })
            },
            ResponseId: "resp-read-next");
    }

    private static ModelResponse BuildFinalAnswerAfterRead(ModelRequest request, string readCallId)
    {
        var readMessage = request.Conversation
            .OfType<ModelToolResultMessage>()
            .Single(message => message.CallId == readCallId);

        TestAssert.Contains("AgentOrchestrator", readMessage.Output);

        return new ModelResponse(
            "Grounded final answer: AgentOrchestrator is implemented in ProjectLens.Application/AgentOrchestrator.cs.",
            ResponseId: "resp-after-read");
    }

    private static ModelResponse BuildFollowUpResponseWithSessionContext(ModelRequest request)
    {
        TestAssert.Contains("Working summary:", request.Instructions);
        TestAssert.Contains("Visited files: README.md", request.Instructions);
        TestAssert.Contains("read_file:", request.Instructions);

        return new ModelResponse(
            "Grounded final answer: the session summary shows that README.md was already inspected.",
            ResponseId: "resp-follow-up");
    }

    private static ModelResponse BuildRefactorFollowUpFromSessionContext(ModelRequest request)
    {
        TestAssert.Contains("For follow-up requests about refactoring", request.Instructions);
        TestAssert.Contains("Likely main flow files: installer.cs", request.Instructions);
        TestAssert.Contains("InstallAsync", request.Instructions);
        TestAssert.Contains("await DiscoverArchives();", request.Instructions);
        TestAssert.Contains("await ExtractArchives();", request.Instructions);
        TestAssert.Contains("observed facts", request.Instructions);
        TestAssert.Contains("Evidence limitations:", request.Instructions);
        TestAssert.Contains("inferred recommendations", request.Instructions);
        TestAssert.Contains("label broader refactor ideas as inferred", request.Instructions);

        return new ModelResponse(
            """
            Observed facts:
            - installer.cs appears to contain the main flow.
            - InstallAsync was observed directly, and the excerpt also showed archive discovery, disk-space validation, and extraction steps in the control flow.
            - The current understanding comes from a bounded read_file excerpt rather than a full repository-wide implementation review.

            Inferred recommendations:
            - A reasonable refactor direction is to keep InstallAsync as the coordinator and extract archive discovery, validation, extraction, and confirmation into smaller responsibilities.
            - Those extractions should be treated as proposed structure, not as classes already observed in the repository.
            """,
            ResponseId: "resp-refactor-follow-up");
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

internal sealed class CountingTool : ITool
{
    private readonly Func<IReadOnlyDictionary<string, string>, ToolExecutionResult> _handler;

    public CountingTool(
        string name,
        string description,
        IReadOnlyDictionary<string, string>? parameters,
        Func<IReadOnlyDictionary<string, string>, ToolExecutionResult> handler)
    {
        Definition = new ToolDefinition(name, description, parameters);
        _handler = handler;
    }

    public ToolDefinition Definition { get; }

    public int ExecutionCount { get; private set; }

    public Task<ToolExecutionResult> ExecuteAsync(
        IReadOnlyDictionary<string, string> arguments,
        CancellationToken cancellationToken = default)
    {
        ExecutionCount++;
        return Task.FromResult(_handler(arguments));
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

    public static void Null(object? value)
    {
        if (value is not null)
        {
            throw new InvalidOperationException($"Expected null, but got '{value}'.");
        }
    }

    public static void NotNull(object? value)
    {
        if (value is null)
        {
            throw new InvalidOperationException("Expected a non-null value.");
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
