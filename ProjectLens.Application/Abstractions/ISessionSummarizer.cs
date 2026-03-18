using ProjectLens.Domain;

namespace ProjectLens.Application.Abstractions;

public interface ISessionSummarizer
{
    string UpdateSummary(
        AgentSessionState sessionState,
        string toolName,
        string toolOutput);
}
