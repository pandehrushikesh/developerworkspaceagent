using ProjectLens.Domain;

namespace ProjectLens.Application.Abstractions;

public interface IInstructionBuilder
{
    string BuildInstructions(AgentSessionState? sessionState);
}
