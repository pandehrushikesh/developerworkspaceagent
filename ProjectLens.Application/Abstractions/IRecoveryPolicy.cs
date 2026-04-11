using ProjectLens.Domain;

namespace ProjectLens.Application.Abstractions;

public interface IRecoveryPolicy
{
    FinalAnswerDecision EvaluateFinalAnswer(
        string? finalAnswer,
        IReadOnlyCollection<ModelToolCall> toolCalls,
        RecoveryState recoveryState,
        AggregatedEvidenceContext? aggregatedEvidenceContext,
        string userPrompt);

    string BuildDuplicateToolCallMessage(ModelToolCall toolCall, AgentSessionState? sessionState);

    RecoveryState UpdateWeakSearchState(
        RecoveryState currentState,
        string toolName,
        string? rawToolOutput,
        string userPrompt);

    bool RequiresMultiFileSynthesis(string userPrompt);
}
