using System.Text;

using static CqrsLens.JsonReportGenerator;

namespace CqrsLens;

/// <summary>Writes the architecture-checks section of a diff report.</summary>
internal static class DiffChecksSectionWriter
{
    public static void Write(
        StringBuilder markdown,
        ArchitectureDiff diff,
        AnalysisResult? headResult,
        string? ciRoot)
    {
        markdown.AppendLine("### Architecture Checks");
        markdown.AppendLine();

        if (!diff.Checks.HasChanges)
        {
            if (diff.Summary.Errors.Head == 0 && diff.Summary.Warnings.Head == 0)
                markdown.AppendLine("All checks passed (no new or resolved findings).");
            else
                markdown.AppendLine(
                    $"No change in architecture check findings " +
                    $"({diff.Summary.Errors.Head} error(s), {diff.Summary.Warnings.Head} warning(s)).");
            markdown.AppendLine();
            return;
        }

        var newErrors = diff.Checks.NewFindings.Count(finding =>
            string.Equals(finding.Severity, "error", StringComparison.OrdinalIgnoreCase));
        var newWarnings = diff.Checks.NewFindings.Count - newErrors;
        markdown.AppendLine(
            $"**{newErrors}** new error(s), **{newWarnings}** new warning(s), " +
            $"**{diff.Checks.ResolvedFindings.Count}** resolved finding(s).");
        markdown.AppendLine();

        if (diff.Checks.NewFindings.Count > 0)
        {
            WriteFindingsTable(
                markdown,
                $"New findings ({diff.Checks.NewFindings.Count})",
                diff.Checks.NewFindings,
                headResult,
                ciRoot,
                isResolved: false);
        }

        if (diff.Checks.ResolvedFindings.Count > 0)
        {
            WriteFindingsTable(
                markdown,
                $"Resolved findings ({diff.Checks.ResolvedFindings.Count})",
                diff.Checks.ResolvedFindings,
                headResult: null,
                ciRoot: null,
                isResolved: true);
        }
    }

    private static void WriteFindingsTable(
        StringBuilder markdown,
        string title,
        IReadOnlyList<JsonCheckFinding> findings,
        AnalysisResult? headResult,
        string? ciRoot,
        bool isResolved)
    {
        markdown.AppendLine($"#### {title}");
        markdown.AppendLine();
        markdown.AppendLine("| Status | Rule | Finding | Location |");
        markdown.AppendLine("| --- | --- | --- | --- |");
        foreach (var finding in findings)
        {
            var status = isResolved
                ? "✅ resolved"
                : string.Equals(finding.Severity, "error", StringComparison.OrdinalIgnoreCase)
                    ? "❌ error"
                    : "⚠️ warning";
            var location = DiffLocationFormatter.Format(finding.Location, headResult, ciRoot);
            markdown.AppendLine(
                $"| {status} | {ReportGenerator.TableCell(finding.Code)} | " +
                $"{ReportGenerator.TableCell(finding.Message)} | {location} |");
        }

        markdown.AppendLine();
    }
}
