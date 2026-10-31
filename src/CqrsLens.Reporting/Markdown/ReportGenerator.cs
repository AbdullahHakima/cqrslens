using System.Text;

namespace CqrsLens;

/// <summary>Public entry point for the Markdown architecture report.</summary>
public static class ReportGenerator
{
    /// <summary>Makes a Mermaid-safe node id from an arbitrary name.</summary>
    public static string MermaidId(string? name) => DiagramBuilder.MermaidId(name);

    /// <summary>Collapses user-derived text that must stay on one report line.</summary>
    internal static string InlineText(string? value)
        => MarkdownEscaping.InlineText(value);

    /// <summary>Escapes text that Markdown must not interpret as HTML.</summary>
    internal static string MarkdownText(string? value)
        => MarkdownEscaping.MarkdownText(value);

    /// <summary>Escapes a value used in a Markdown table cell.</summary>
    internal static string TableCell(string? value)
        => MarkdownEscaping.TableCell(value);

    /// <summary>Renders a Markdown code span, widening the fence when needed.</summary>
    internal static string CodeSpan(string? value)
        => MarkdownEscaping.CodeSpan(value);

    public static string Generate(AnalysisResult result)
        => MarkdownReportWriter.Generate(result);

    internal static void AppendDiagnosticsMarkdown(
        StringBuilder md,
        IReadOnlyList<AnalysisDiagnostic> diagnostics)
        => MarkdownDiagnosticsWriter.AppendDiagnosticsMarkdown(md, diagnostics);

    internal static void AppendChecksMarkdown(
        StringBuilder md,
        IReadOnlyList<CheckFinding> checks,
        bool baselineApplied = false)
        => MarkdownChecksWriter.AppendChecksMarkdown(md, checks, baselineApplied);

    /// <summary>The one-line timing footer used at the end of every report.</summary>
    public static string TimingFooter(AnalysisResult result)
        => MarkdownReportWriter.TimingFooter(result);
}
