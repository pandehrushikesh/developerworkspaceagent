using ProjectLens.Application.Abstractions;

namespace ProjectLens.Application;

public sealed class RuleBasedEvidenceEvaluator : IEvidenceEvaluator
{
    public EvidenceAssessment Assess(IEnumerable<EvidenceItem> evidenceItems)
    {
        ArgumentNullException.ThrowIfNull(evidenceItems);

        var items = evidenceItems
            .Where(item => !string.IsNullOrWhiteSpace(item.Content))
            .ToArray();

        if (items.Length == 0)
        {
            return new EvidenceAssessment(
                false,
                0,
                0,
                "Insufficient: no evidence items were available.",
                ["No evidence items were available."]);
        }

        var distinctSourceCount = items
            .Select(item => item.SourceId)
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var directSourceCount = items
            .Where(item => item.Kind == EvidenceKind.DirectSnippet)
            .Select(item => item.SourceId)
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var directOrSummaryCount = items.Count(item =>
            item.Kind is EvidenceKind.DirectSnippet or EvidenceKind.FileSummary);
        var searchHitCount = items.Count(item => item.Kind == EvidenceKind.SearchHit);
        var partialRatio = (double)items.Count(item => item.IsPartial) / items.Length;
        var onlySearchHits = items.All(item => item.Kind == EvidenceKind.SearchHit);
        var hasFileSummary = items.Any(item => item.Kind == EvidenceKind.FileSummary);

        var coverageScore = CalculateCoverageScore(directOrSummaryCount, searchHitCount, distinctSourceCount);
        var confidenceScore = Math.Clamp(items.Average(CalculateItemConfidence), 0, 1);
        var missingAreas = BuildMissingAreas(
            directSourceCount,
            distinctSourceCount,
            partialRatio,
            onlySearchHits);
        var isSufficient =
            coverageScore >= 0.65 &&
            confidenceScore >= 0.65 &&
            (directSourceCount >= 2 || (directSourceCount >= 1 && hasFileSummary && distinctSourceCount >= 2));
        var reason = BuildReason(
            isSufficient,
            onlySearchHits,
            directSourceCount,
            partialRatio);

        return new EvidenceAssessment(
            isSufficient,
            coverageScore,
            confidenceScore,
            reason,
            missingAreas);
    }

    private static double CalculateCoverageScore(
        int directOrSummaryCount,
        int searchHitCount,
        int distinctSourceCount)
    {
        var sourceDiversityScore = distinctSourceCount switch
        {
            0 => 0,
            1 => 0.25,
            2 => 0.55,
            _ => 0.75
        };

        return Math.Clamp(
            (directOrSummaryCount * 0.25) + (searchHitCount * 0.08) + sourceDiversityScore,
            0,
            1);
    }

    private static double CalculateItemConfidence(EvidenceItem item)
    {
        var kindWeight = item.Kind switch
        {
            EvidenceKind.DirectSnippet => 1.0,
            EvidenceKind.FileSummary => 0.75,
            EvidenceKind.SearchHit => 0.45,
            EvidenceKind.ToolObservation => 0.25,
            _ => 0.25
        };
        var partialWeight = item.IsPartial ? 0.75 : 1.0;

        return Math.Clamp(item.Confidence, 0, 1) * kindWeight * partialWeight;
    }

    private static IReadOnlyCollection<string> BuildMissingAreas(
        int directSourceCount,
        int distinctSourceCount,
        double partialRatio,
        bool onlySearchHits)
    {
        var missingAreas = new List<string>();

        if (directSourceCount == 0)
        {
            missingAreas.Add("No direct file evidence was read.");
        }

        if (distinctSourceCount < 2)
        {
            missingAreas.Add("Evidence covers fewer than two distinct sources.");
        }

        if (partialRatio > 0.5)
        {
            missingAreas.Add("Most evidence is partial, truncated, or summary-level.");
        }

        if (onlySearchHits)
        {
            missingAreas.Add("Evidence is limited to search hits and should be confirmed with read_file.");
        }

        return missingAreas;
    }

    private static string BuildReason(
        bool isSufficient,
        bool onlySearchHits,
        int directSourceCount,
        double partialRatio)
    {
        if (isSufficient)
        {
            return "Sufficient: evidence includes direct file evidence across multiple sources.";
        }

        if (onlySearchHits)
        {
            return "Insufficient: only search hits are available; no direct file evidence was read.";
        }

        if (directSourceCount == 1)
        {
            return "Partially sufficient: direct evidence exists, but coverage is limited to one source.";
        }

        if (partialRatio > 0.5)
        {
            return "Partially sufficient: evidence is mostly partial or summary-level, reducing confidence.";
        }

        return "Insufficient: evidence coverage or confidence is below the required threshold.";
    }
}
