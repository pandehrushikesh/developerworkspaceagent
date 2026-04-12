using ProjectLens.Application.Abstractions;
using ProjectLens.Domain;

namespace ProjectLens.Application;

public sealed record AgentOrchestrationDependencies
{
    public AgentOrchestrationDependencies(
        IPromptClarifier promptClarifier,
        IInstructionBuilder instructionBuilder,
        ISessionContextService sessionContextService,
        IRecoveryPolicy recoveryPolicy,
        IToolOutputAdapter toolOutputAdapter,
        IEvidenceEvaluator? evidenceEvaluator = null,
        IConvergencePolicy? convergencePolicy = null)
    {
        PromptClarifier = promptClarifier ?? throw new ArgumentNullException(nameof(promptClarifier));
        InstructionBuilder = instructionBuilder ?? throw new ArgumentNullException(nameof(instructionBuilder));
        SessionContextService = sessionContextService ?? throw new ArgumentNullException(nameof(sessionContextService));
        RecoveryPolicy = recoveryPolicy ?? throw new ArgumentNullException(nameof(recoveryPolicy));
        ToolOutputAdapter = toolOutputAdapter ?? throw new ArgumentNullException(nameof(toolOutputAdapter));
        EvidenceEvaluator = evidenceEvaluator ?? new RuleBasedEvidenceEvaluator();
        ConvergencePolicy = convergencePolicy ?? new RuleBasedConvergencePolicy();
    }

    public IPromptClarifier PromptClarifier { get; }

    public IInstructionBuilder InstructionBuilder { get; }

    public ISessionContextService SessionContextService { get; }

    public IRecoveryPolicy RecoveryPolicy { get; }

    public IToolOutputAdapter ToolOutputAdapter { get; }

    public IEvidenceEvaluator EvidenceEvaluator { get; }

    public IConvergencePolicy ConvergencePolicy { get; }

    public static AgentOrchestrationDependencies CreateDefault(
        IAgentSessionStore? sessionStore = null,
        IFileCompressor? fileCompressor = null,
        ISessionSummarizer? sessionSummarizer = null,
        IEvidenceQualityEvaluator? evidenceQualityEvaluator = null,
        IPromptClarifier? promptClarifier = null)
    {
        var recoveryPolicy = new DefaultRecoveryPolicy(evidenceQualityEvaluator);

        return new AgentOrchestrationDependencies(
            promptClarifier ?? new RuleBasedPromptClarifier(),
            new DefaultInstructionBuilder(),
            new DefaultSessionContextService(sessionStore, sessionSummarizer, evidenceQualityEvaluator),
            recoveryPolicy,
            new DefaultToolOutputAdapter(fileCompressor, evidenceQualityEvaluator, recoveryPolicy),
            new RuleBasedEvidenceEvaluator(),
            new RuleBasedConvergencePolicy());
    }
}
