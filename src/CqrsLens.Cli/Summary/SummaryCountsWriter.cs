using System.Text;

namespace CqrsLens;

/// <summary>Writes the summary header, shape counts and optional diff headline.</summary>
internal static class SummaryCountsWriter
{
    public static void Write(StringBuilder markdown, AnalysisResult result, ArchitectureDiff? diff)
    {
        markdown.AppendLine("## CqrsLens architecture summary");
        markdown.AppendLine();
        markdown.AppendLine(
            $"Scanned **{result.Projects.Count}** project(s) with the " +
            $"**{ReportGenerator.InlineText(result.ScannerName)}** scanner on " +
            $"{result.GeneratedAt.UtcDateTime:yyyy-MM-dd HH:mm:ss} UTC.");
        markdown.AppendLine();
        markdown.AppendLine("| Requests | Handlers | Validators | Behaviors | DbContexts |");
        markdown.AppendLine("| ---: | ---: | ---: | ---: | ---: |");
        markdown.AppendLine(
            $"| {result.Requests.Count} | {result.Handlers.Count} | {result.Validators.Count} | " +
            $"{result.Behaviors.Count} | {result.DbContexts.Count} |");
        markdown.AppendLine();

        if (diff is null)
            return;

        markdown.AppendLine("### Architecture diff");
        markdown.AppendLine();
        markdown.AppendLine(ReportGenerator.InlineText(ArchitectureDiffer.FormatConsoleSummary(diff)));
        markdown.AppendLine();
    }
}
