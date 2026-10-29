using System.Text;

namespace CqrsLens;

/// <summary>Writes baseline-aware architecture check counts and the first N findings.</summary>
internal static class SummaryChecksWriter
{
    private const int MaxNewFindings = 10;
    private const int MaxKnownFindings = 5;

    public static void Write(StringBuilder markdown, AnalysisResult result, string? ciRoot)
    {
        markdown.AppendLine("### Architecture checks");
        markdown.AppendLine();

        if (result.Checks.Count == 0)
        {
            markdown.AppendLine("All checks passed.");
            return;
        }

        var newChecks = result.BaselineApplied
            ? result.Checks.Where(finding => !finding.IsKnown).ToList()
            : result.Checks;
        var knownChecks = result.BaselineApplied
            ? result.Checks.Where(finding => finding.IsKnown).ToList()
            : new List<CheckFinding>();

        WriteCounts(markdown, result, newChecks, knownChecks);

        markdown.AppendLine();
        AppendFindings(markdown, result, ciRoot, "New findings", newChecks, MaxNewFindings);
        if (knownChecks.Count > 0)
            AppendFindings(markdown, result, ciRoot, "Known findings", knownChecks, MaxKnownFindings);
    }

    private static void WriteCounts(
        StringBuilder markdown,
        AnalysisResult result,
        IReadOnlyList<CheckFinding> newChecks,
        IReadOnlyList<CheckFinding> knownChecks)
    {
        var newErrors = newChecks.Count(finding => finding.Severity == CheckSeverity.Error);
        var newWarnings = newChecks.Count - newErrors;

        if (result.BaselineApplied)
        {
            markdown.AppendLine(
                $"**{newErrors}** new error(s), **{newWarnings}** new warning(s); " +
                $"{knownChecks.Count} known finding(s) from the baseline.");
            if (newChecks.Count == 0)
            {
                markdown.AppendLine();
                markdown.AppendLine("No new findings.");
            }

            return;
        }

        markdown.AppendLine($"**{newErrors}** error(s), **{newWarnings}** warning(s).");
    }

    private static void AppendFindings(
        StringBuilder markdown,
        AnalysisResult result,
        string? ciRoot,
        string title,
        IReadOnlyList<CheckFinding> findings,
        int limit)
    {
        if (findings.Count == 0)
            return;

        markdown.AppendLine($"#### {title}");
        markdown.AppendLine();
        markdown.AppendLine("| Severity | Rule | Finding | Location |");
        markdown.AppendLine("| --- | --- | --- | --- |");
        foreach (var finding in findings.Take(limit))
        {
            var severity = finding.Severity == CheckSeverity.Error ? "error" : "warning";
            var location = finding.Location is null
                ? "—"
                : ReportGenerator.TableCell(
                    $"{CiPath.Relative(result, finding.Location.File, ciRoot)}:" +
                    $"{finding.Location.Line}:{finding.Location.Column}");
            markdown.AppendLine(
                $"| {severity} | {ReportGenerator.TableCell(finding.Code)} | " +
                $"{ReportGenerator.TableCell(finding.Message)} | {location} |");
        }

        if (findings.Count > limit)
        {
            markdown.AppendLine();
            markdown.AppendLine($"_…and {findings.Count - limit} more in the full report._");
        }

        markdown.AppendLine();
    }
}
