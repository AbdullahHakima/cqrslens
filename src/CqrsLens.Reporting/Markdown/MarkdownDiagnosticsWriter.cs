using System.Text;

namespace CqrsLens;

/// <summary>Writes scan and analysis diagnostic sections.</summary>
internal static class MarkdownDiagnosticsWriter
{
    internal static void WriteScanDiagnostics(StringBuilder md, AnalysisResult result)
    {
        if (result.SkippedFiles.Count == 0)
            return;

        md.AppendLine("## Scan Diagnostics");
        md.AppendLine();
        md.AppendLine($"{result.FileCount} of {result.DiscoveredFileCount} source files scanned. " +
                      $"{result.SkippedFiles.Count} skipped:");
        md.AppendLine();
        foreach (var file in result.SkippedFiles)
            md.AppendLine($"- {MarkdownEscaping.CodeSpan(file)}");
        md.AppendLine();
    }

    internal static void WriteAnalysisDiagnostics(StringBuilder md, AnalysisResult result)
    {
        md.AppendLine("## Analysis Diagnostics");
        md.AppendLine();
        AppendDiagnosticsMarkdown(md, result.Diagnostics);
        md.AppendLine();
    }

    internal static void AppendDiagnosticsMarkdown(
        StringBuilder md,
        IReadOnlyList<AnalysisDiagnostic> diagnostics)
    {
        if (diagnostics.Count == 0)
        {
            md.AppendLine("No scan diagnostics.");
            return;
        }

        var errors = diagnostics.Count(d => d.Severity == AnalysisDiagnosticSeverity.Error);
        var warnings = diagnostics.Count(d => d.Severity == AnalysisDiagnosticSeverity.Warning);
        var info = diagnostics.Count - errors - warnings;
        md.AppendLine($"{errors} error(s), {warnings} warning(s), {info} info item(s):");
        md.AppendLine();

        foreach (var diagnostic in diagnostics)
        {
            var badge = diagnostic.Severity switch
            {
                AnalysisDiagnosticSeverity.Error => "❌ Error",
                AnalysisDiagnosticSeverity.Warning => "⚠️ Warning",
                _ => "ℹ️ Info",
            };
            var locality = diagnostic.Location is null
                ? ""
                : $" {MarkdownEscaping.CodeSpan($"{diagnostic.Location.File}:{diagnostic.Location.Line}:{diagnostic.Location.Column}")}";
            var project = string.IsNullOrEmpty(diagnostic.ProjectName)
                ? ""
                : $" ({MarkdownEscaping.InlineText(diagnostic.ProjectName)})";
            md.AppendLine(
                $"- {badge} **{MarkdownEscaping.InlineText(diagnostic.Code)}** — {MarkdownEscaping.InlineText(diagnostic.Message)}{locality}{project}");
        }
    }
}
