using System.Text;

namespace CqrsLens;

/// <summary>Writes the project and layer dependency diagram sections.</summary>
internal static class MarkdownDiagramWriter
{
    internal static void WriteProjectGraph(StringBuilder md, AnalysisResult result)
    {
        md.AppendLine("## Project Dependency Graph");
        md.AppendLine();
        var projectGraph = DiagramBuilder.ProjectGraph(result);
        if (projectGraph is null)
        {
            md.AppendLine("_No projects found._");
        }
        else
        {
            AppendDiagramOrLimitNote(md, projectGraph, "project graph");
            md.AppendLine();
            if (result.Projects
                .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Any(g => g.Count() > 1))
            {
                md.AppendLine("_Project references are drawn only when the target name resolves to a single " +
                              "project; ambiguous duplicate names are reported under Architecture Checks._");
                md.AppendLine();
            }
            md.AppendLine(LayerLegend(result));
        }
        md.AppendLine();
    }

    internal static void WriteLayers(StringBuilder md, AnalysisResult result)
    {
        md.AppendLine("## Layers");
        md.AppendLine();
        var layerGraph = DiagramBuilder.LayerGraph(result);
        if (layerGraph is null)
        {
            md.AppendLine("_No projects found._");
        }
        else
        {
            AppendDiagramOrLimitNote(md, layerGraph, "layer graph");
            md.AppendLine();
            md.AppendLine("_Dashed boundaries group projects into layers. Projects are grouped by Clean " +
                          "Architecture naming convention (Domain / Application / Infrastructure / Api / " +
                          "Web / UI); projects matching no convention are grouped by their top-level folder._");
        }
        md.AppendLine();
    }

    internal static void WriteFlow(StringBuilder md, AnalysisResult result)
    {
        var sections = DiagramBuilder.FlowDiagramSections(result);
        if (sections.Count == 0)
            return;

        md.AppendLine("## Flow");
        md.AppendLine();
        var totalEdges = sections.Sum(s => s.Edges.Count);
        var edgeText = totalEdges == 1 ? "1 resolved flow edge" : $"{totalEdges} resolved flow edges";
        var sectionText = sections.Count == 1 ? "1 diagram" : $"{sections.Count} feature/project diagrams";
        var unresolvedNote = result.UnresolvedDispatches.Count > 0
            ? $" {result.UnresolvedDispatches.Count} unresolved dispatch call(s) are not drawn as edges."
            : "";
        md.AppendLine(
            $"_{edgeText} across {sectionText} " +
            "(endpoint → request → handler and onward; DbContext nodes appear only when a handler " +
            "directly references a discovered DbContext type; " +
            $"every flow edge appears in exactly one diagram).{unresolvedNote}_");
        md.AppendLine();
        md.AppendLine("**Legend:** blue stadium = endpoint · blue = request · gray = handler · " +
                      "fuchsia = notification · green = DbContext.");
        md.AppendLine();

        foreach (var section in sections)
        {
            if (sections.Count > 1 || section.Label is not null)
            {
                md.AppendLine($"### {MarkdownEscaping.MarkdownText(section.Heading)}");
                md.AppendLine();
            }

            if (!DiagramBuilder.IsWithinMermaidLimits(section.Source))
            {
                md.AppendLine($"_Diagram omitted: {section.Edges.Count} edges exceed the CqrsLens " +
                              "safety limit (45,000 characters / 400 edges)._");
            }
            else
            {
                AppendMermaid(md, section.Source);
                md.AppendLine();
                md.AppendLine($"_{MarkdownEscaping.MarkdownText(section.Caption)}_");
            }
            md.AppendLine();
        }
    }

    internal static void AppendMermaid(StringBuilder md, string source)
    {
        md.AppendLine("```mermaid");
        md.AppendLine(source);
        md.AppendLine("```");
    }

    internal static void AppendDiagramOrLimitNote(StringBuilder md, string source, string label)
    {
        if (!DiagramBuilder.IsWithinMermaidLimits(source))
        {
            md.AppendLine($"_Diagram omitted: the {label} exceeds the CqrsLens safety limit " +
                          "(45,000 characters / 400 edges)._");
            return;
        }

        AppendMermaid(md, source);
    }

    private static string LayerLegend(AnalysisResult result)
    {
        var text = "**Layer colors:** green = Domain · blue = Application · amber = Infrastructure · " +
                   "purple = Api/Web/UI · gray = Tests/other.";
        if (result.Checks.Any(c =>
                c.Code is ArchitectureChecker.LayerViolationCode or ArchitectureChecker.ApplicationLayerViolationCode))
            text += " Red dashed edges mark `LayerViolation` findings.";
        return text;
    }
}
