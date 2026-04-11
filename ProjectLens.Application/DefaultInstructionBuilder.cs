using System.Text;
using ProjectLens.Application.Abstractions;
using ProjectLens.Domain;

namespace ProjectLens.Application;

public sealed class DefaultInstructionBuilder : IInstructionBuilder
{
    public string BuildInstructions(AgentSessionState? sessionState)
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

    private static bool IsProvisionalFeatureContext(string? workingSummary)
    {
        return !string.IsNullOrWhiteSpace(workingSummary) &&
            (workingSummary.Contains("Feature flow confidence: provisional", StringComparison.OrdinalIgnoreCase) ||
             workingSummary.Contains("Current feature-flow understanding is provisional", StringComparison.OrdinalIgnoreCase));
    }
}
