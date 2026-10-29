using System.Text;
using static CqrsLens.HtmlReportText;

namespace CqrsLens;

/// <summary>Renders scan, analysis and architecture-check diagnostics.</summary>
internal static class HtmlDiagnosticsSectionRenderer
{
    internal static void AppendScan(StringBuilder html, AnalysisResult result)
    {
        if (result.SkippedFiles.Count > 0)
        {
            html.AppendLine("<h2>Scan Diagnostics</h2>");
            html.AppendLine($"<p>{result.FileCount} of {result.DiscoveredFileCount} source files scanned. " +
                            $"{result.SkippedFiles.Count} skipped:</p>");
            html.AppendLine("<ul>");
            foreach (var file in result.SkippedFiles)
                html.AppendLine($"<li><code>{Encode(file)}</code></li>");
            html.AppendLine("</ul>");
        }
    }

    internal static void AppendAnalysis(StringBuilder html, AnalysisResult result)
    {
        // ---- Analysis diagnostics ---------------------------------------------------------
        html.AppendLine("<h2>Analysis Diagnostics</h2>");
        if (result.Diagnostics.Count == 0)
        {
            html.AppendLine("<p>No scan diagnostics.</p>");
        }
        else
        {
            var diagnosticErrors = result.Diagnostics.Count(
                d => d.Severity == AnalysisDiagnosticSeverity.Error);
            var diagnosticWarnings = result.Diagnostics.Count(
                d => d.Severity == AnalysisDiagnosticSeverity.Warning);
            var diagnosticInfo = result.Diagnostics.Count - diagnosticErrors - diagnosticWarnings;
            html.AppendLine(
                $"<p>{diagnosticErrors} error(s), {diagnosticWarnings} warning(s), " +
                $"{diagnosticInfo} info item(s):</p>");
            html.AppendLine("<ul class=\"checks\">");
            foreach (var diagnostic in result.Diagnostics)
            {
                var cls = diagnostic.Severity switch
                {
                    AnalysisDiagnosticSeverity.Error => "error",
                    AnalysisDiagnosticSeverity.Warning => "warning",
                    _ => "info",
                };
                var badge = diagnostic.Severity switch
                {
                    AnalysisDiagnosticSeverity.Error => "❌ Error",
                    AnalysisDiagnosticSeverity.Warning => "⚠️ Warning",
                    _ => "ℹ️ Info",
                };
                var locality = diagnostic.Location is null
                    ? ""
                    : $" <code>{Encode(diagnostic.Location.File)}:{diagnostic.Location.Line}:{diagnostic.Location.Column}</code>";
                var project = string.IsNullOrEmpty(diagnostic.ProjectName)
                    ? ""
                    : $" ({Encode(diagnostic.ProjectName)})";
                html.AppendLine(
                    $"<li class=\"{cls}\">{badge} <strong>{Encode(diagnostic.Code)}</strong> — " +
                    $"{Encode(diagnostic.Message)}{locality}{project}</li>");
            }
            html.AppendLine("</ul>");
        }
    }

    internal static void AppendChecks(StringBuilder html, AnalysisResult result)
    {
        // ---- Checks -----------------------------------------------------------------------
        html.AppendLine("<h2>Architecture Checks</h2>");
        if (result.Checks.Count == 0)
        {
            html.AppendLine("<p>All checks passed.</p>");
        }
        else
        {
            var newChecks = result.BaselineApplied
                ? result.Checks.Where(c => !c.IsKnown).ToList()
                : result.Checks;
            var knownChecks = result.BaselineApplied
                ? result.Checks.Where(c => c.IsKnown).ToList()
                : new List<CheckFinding>();

            var errors = newChecks.Count(c => c.Severity == CheckSeverity.Error);
            var warnings = newChecks.Count - errors;
            if (result.BaselineApplied)
            {
                html.AppendLine(
                    $"<p>{errors} new error(s), {warnings} new warning(s); " +
                    $"{knownChecks.Count} known finding(s) from the baseline:</p>");
                if (newChecks.Count == 0)
                    html.AppendLine("<p>No new findings.</p>");
            }
            else
            {
                html.AppendLine($"<p>{errors} error(s), {warnings} warning(s):</p>");
            }

            html.AppendLine("<ul class=\"checks\">");
            foreach (var check in newChecks)
                HtmlCheckRenderer.Append(html, check, known: false);
            foreach (var check in knownChecks)
                HtmlCheckRenderer.Append(html, check, known: true);
            html.AppendLine("</ul>");
        }
    }
}