using ProjectLens.Domain;

namespace ProjectLens.Application.Abstractions;

public interface IPromptClarifier
{
    PromptClarification? GetClarification(
        string userPrompt,
        AgentSessionState? sessionState);
}
