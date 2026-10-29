using System.Text;

namespace CqrsLens;

/// <summary>
/// Compact Markdown intended for $GITHUB_STEP_SUMMARY. The full architecture
/// report remains the primary artifact; this surfaces counts and actionable
/// findings next to the job log.
/// </summary>
public static class SummaryGenerator
{
    public static string Generate(AnalysisResult result, string? ciRoot = null)
        => Generate(result, ciRoot, diff: null);

    internal static string Generate(
        AnalysisResult result,
        string? ciRoot,
        ArchitectureDiff? diff)
    {
        var markdown = new StringBuilder();
        SummaryCountsWriter.Write(markdown, result, diff);
        SummaryChecksWriter.Write(markdown, result, ciRoot);
        markdown.AppendLine();
        SummaryDiagnosticsWriter.Write(markdown, result, ciRoot);
        return markdown.ToString();
    }
}
