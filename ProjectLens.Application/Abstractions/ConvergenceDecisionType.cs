namespace ProjectLens.Application.Abstractions;

public enum ConvergenceDecisionType
{
    ContinueWithBroaderSearch,
    ContinueWithDeeperRead,
    FinalizePartialAnswer,
    FinalizeConfidentAnswer
}
