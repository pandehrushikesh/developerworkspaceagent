namespace ProjectLens.Application.Abstractions;

public interface IEvidenceQualityEvaluator
{
    bool IsLowValuePath(string path);

    bool IsMeaningfulSourcePath(string path);

    bool IsFeatureTracingPrompt(string? userPrompt);

    bool IsConceptualQuery(string? userPrompt);

    IReadOnlyCollection<string> ExpandIntentTerms(string? userPrompt);

    int ScoreFile(
        string path,
        string? snippet = null,
        string? userPrompt = null);

    IReadOnlyCollection<EvidenceMatch> RankMatches(
        IEnumerable<EvidenceMatch> matches,
        string? userPrompt = null,
        int maxResults = 5);

    SearchEvidenceAssessment AssessSearchEvidence(
        IEnumerable<EvidenceMatch> matches,
        string? userPrompt = null,
        int maxResults = 5);

    IReadOnlyCollection<string> SelectPathsForSessionMemory(
        IEnumerable<string> paths,
        int maxResults);
}
