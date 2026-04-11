using ProjectLens.Domain;

namespace ProjectLens.Application.Abstractions;

public interface IToolOutputAdapter
{
    ToolOutputAdapterResult BuildToolConversationOutput(
        ModelToolCall toolCall,
        ToolExecutionResult executionResult,
        string userPrompt,
        AggregatedEvidenceContext? aggregatedEvidenceContext);
}
