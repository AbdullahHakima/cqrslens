using System.Text;

namespace CqrsLens;

/// <summary>Writes the per-project Direct Flows section for non-CQRS entrypoints.</summary>
internal static class MarkdownDirectFlowWriter
{
    internal static void Write(
        StringBuilder md,
        AnalysisResult result,
        string? focusService = null,
        bool excludeTests = false)
    {
        var sections = DirectFlowDiagramBuilder.Build(result, focusService, excludeTests);
        if (sections.Count == 0)
            return;

        md.AppendLine("## Direct Flows");
        md.AppendLine();
        md.AppendLine("_Non-CQRS execution paths (HTTP Direct, gRPC, Background Workers, and Event Consumers) " +
                      "traced from entrypoints through injected services to DbContexts and published integration events._");
        md.AppendLine();
        md.AppendLine("**Legend:** blue stadium = HTTP/UI entrypoint · indigo stadium = gRPC · amber stadium = Worker · " +
                      "purple stadium = Event consumer · gray = Service · green = DbContext · gold dashed = Published event.");
        md.AppendLine();

        foreach (var section in sections)
        {
            if (sections.Count > 1 || !string.IsNullOrWhiteSpace(section.ProjectName))
            {
                md.AppendLine($"### {MarkdownEscaping.MarkdownText(section.Heading)}");
                md.AppendLine();
            }

            MarkdownDiagramWriter.AppendDiagramOrLimitNote(md, section.Source, "direct flow");
            md.AppendLine();
            md.AppendLine($"_{MarkdownEscaping.MarkdownText(section.Caption)}_");
            md.AppendLine();
        }
    }
}
