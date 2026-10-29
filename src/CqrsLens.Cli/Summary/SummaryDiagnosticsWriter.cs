using System.Text;

namespace CqrsLens;

/// <summary>Writes the scan diagnostics section of the job summary.</summary>
internal static class SummaryDiagnosticsWriter
{
    private const int MaxDiagnostics = 5;

    public static void Write(StringBuilder markdown, AnalysisResult result, string? ciRoot)
    {
        markdown.AppendLine("### Scan diagnostics");
        markdown.AppendLine();

        if (result.Diagnostics.Count == 0)
        {
            markdown.AppendLine("No scan diagnostics.");
            return;
        }

        var errors = result.Diagnostics.Count(diagnostic =>
            diagnostic.Severity == AnalysisDiagnosticSeverity.Error);
        var warnings = result.Diagnostics.Count(diagnostic =>
            diagnostic.Severity == AnalysisDiagnosticSeverity.Warning);
        var info = result.Diagnostics.Count - errors - warnings;
        markdown.AppendLine($"{errors} error(s), {warnings} warning(s), {info} info item(s).");

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
            markdown.AppendLine(
                $"- {badge} **{ReportGenerator.InlineText(diagnostic.Code)}** — " +
                $"{ReportGenerator.InlineText(diagnostic.Message)}{location}");
        }

        if (result.Diagnostics.Count > MaxDiagnostics)
            markdown.AppendLine($"- …and {result.Diagnostics.Count - MaxDiagnostics} more in the full report.");
    }
}
