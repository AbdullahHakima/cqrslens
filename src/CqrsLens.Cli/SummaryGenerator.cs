using System.Text;

namespace CqrsLens;

/// <summary>
/// Compact Markdown intended for $GITHUB_STEP_SUMMARY. The full architecture
/// report remains the primary artifact; this surfaces counts and actionable
/// findings next to the job log.
/// </summary>
public static class SummaryGenerator
{
    private const int MaxNewFindings = 10;
    private const int MaxKnownFindings = 5;
    private const int MaxDiagnostics = 5;

    public static string Generate(AnalysisResult result, string? ciRoot = null)
        => Generate(result, ciRoot, diff: null);

    internal static string Generate(
        AnalysisResult result,
        string? ciRoot,
        ArchitectureDiff? diff)
    {
        var md = new StringBuilder();
        md.AppendLine("## CqrsLens architecture summary");
        md.AppendLine();
        md.AppendLine(
            $"Scanned **{result.Projects.Count}** project(s) with the " +
            $"**{ReportGenerator.InlineText(result.ScannerName)}** scanner on " +
            $"{result.GeneratedAt.UtcDateTime:yyyy-MM-dd HH:mm:ss} UTC.");
        md.AppendLine();
        md.AppendLine("| Requests | Handlers | Validators | Behaviors | DbContexts |");
        md.AppendLine("| ---: | ---: | ---: | ---: | ---: |");
        md.AppendLine(
            $"| {result.Requests.Count} | {result.Handlers.Count} | {result.Validators.Count} | " +
            $"{result.Behaviors.Count} | {result.DbContexts.Count} |");
        md.AppendLine();

        if (diff is not null)
        {
            md.AppendLine("### Architecture diff");
            md.AppendLine();
            md.AppendLine(ReportGenerator.InlineText(ArchitectureDiffer.FormatConsoleSummary(diff)));
            md.AppendLine();
        }

        md.AppendLine("### Architecture checks");
        md.AppendLine();
        if (result.Checks.Count == 0)
        {
            md.AppendLine("All checks passed.");
        }
        else
        {
            var newChecks = result.BaselineApplied
                ? result.Checks.Where(c => !c.IsKnown).ToList()
                : result.Checks;
            var knownChecks = result.BaselineApplied
                ? result.Checks.Where(c => c.IsKnown).ToList()
                : new List<CheckFinding>();
            var newErrors = newChecks.Count(c => c.Severity == CheckSeverity.Error);
            var newWarnings = newChecks.Count - newErrors;

            if (result.BaselineApplied)
            {
                md.AppendLine(
                    $"**{newErrors}** new error(s), **{newWarnings}** new warning(s); " +
                    $"{knownChecks.Count} known finding(s) from the baseline.");
                if (newChecks.Count == 0)
                {
                    md.AppendLine();
                    md.AppendLine("No new findings.");
                }
            }
            else
            {
                var errors = newChecks.Count(c => c.Severity == CheckSeverity.Error);
                var warnings = newChecks.Count - errors;
                md.AppendLine($"**{errors}** error(s), **{warnings}** warning(s).");
            }

            md.AppendLine();
            AppendFindings(md, result, ciRoot, "New findings", newChecks, MaxNewFindings);
            if (knownChecks.Count > 0)
                AppendFindings(md, result, ciRoot, "Known findings", knownChecks, MaxKnownFindings);
        }

        md.AppendLine();
        md.AppendLine("### Scan diagnostics");
        md.AppendLine();
        if (result.Diagnostics.Count == 0)
        {
            md.AppendLine("No scan diagnostics.");
        }
        else
        {
            var errors = result.Diagnostics.Count(d => d.Severity == AnalysisDiagnosticSeverity.Error);
            var warnings = result.Diagnostics.Count(d => d.Severity == AnalysisDiagnosticSeverity.Warning);
            var info = result.Diagnostics.Count - errors - warnings;
            md.AppendLine($"{errors} error(s), {warnings} warning(s), {info} info item(s).");
            foreach (var diagnostic in result.Diagnostics.Take(MaxDiagnostics))
            {
                var badge = diagnostic.Severity switch
                {
                    AnalysisDiagnosticSeverity.Error => "❌",
                    AnalysisDiagnosticSeverity.Warning => "⚠️",
                    _ => "ℹ️",
                };
                var location = diagnostic.Location is null
                    ? ""
                    : $" ({ReportGenerator.CodeSpan(
                        $"{CiPath.Relative(result, diagnostic.Location.File, ciRoot)}:{diagnostic.Location.Line}")})";
                md.AppendLine(
                    $"- {badge} **{ReportGenerator.InlineText(diagnostic.Code)}** — " +
                    $"{ReportGenerator.InlineText(diagnostic.Message)}{location}");
            }

            if (result.Diagnostics.Count > MaxDiagnostics)
                md.AppendLine($"- …and {result.Diagnostics.Count - MaxDiagnostics} more in the full report.");
        }

        return md.ToString();
    }

    private static void AppendFindings(
        StringBuilder md,
        AnalysisResult result,
        string? ciRoot,
        string title,
        IReadOnlyList<CheckFinding> findings,
        int limit)
    {
        if (findings.Count == 0)
            return;

        md.AppendLine($"#### {title}");
        md.AppendLine();
        md.AppendLine("| Severity | Rule | Finding | Location |");
        md.AppendLine("| --- | --- | --- | --- |");
        foreach (var finding in findings.Take(limit))
        {
            var severity = finding.Severity == CheckSeverity.Error ? "error" : "warning";
            var location = finding.Location is null
                ? "—"
                : ReportGenerator.TableCell(
                    $"{CiPath.Relative(result, finding.Location.File, ciRoot)}:" +
                    $"{finding.Location.Line}:{finding.Location.Column}");
            md.AppendLine(
                $"| {severity} | {ReportGenerator.TableCell(finding.Code)} | " +
                $"{ReportGenerator.TableCell(finding.Message)} | {location} |");
        }

        if (findings.Count > limit)
        {
            md.AppendLine();
            md.AppendLine($"_…and {findings.Count - limit} more in the full report._");
        }

        md.AppendLine();
    }
}
