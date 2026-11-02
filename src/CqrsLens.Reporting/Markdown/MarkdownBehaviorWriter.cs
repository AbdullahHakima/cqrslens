using System.Text;

namespace CqrsLens;

/// <summary>Writes the pipeline behaviors chain diagram and table.</summary>
internal static class MarkdownBehaviorWriter
{
    internal static void Write(StringBuilder md, AnalysisResult result)
    {
        md.AppendLine("## Pipeline Behaviors");
        md.AppendLine();
        if (result.Behaviors.Count == 0)
        {
            md.AppendLine("_No IPipelineBehavior implementations found._");
            md.AppendLine();
            return;
        }

        var chain = DiagramBuilder.BehaviorChain(result);
        md.AppendLine("_Behaviors wrap every request that flows through the pipeline. The diagram lists " +
                      "them in the order the model recorded them (scan order, not resolved execution order)._");
        md.AppendLine();
        if (chain is not null)
        {
            MarkdownDiagramWriter.AppendDiagramOrLimitNote(md, chain, "behavior chain");
            md.AppendLine();
        }

        md.AppendLine("| Behavior | Project | Source |");
        md.AppendLine("| --- | --- | --- |");
        foreach (var b in result.Behaviors.OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase))
        {
            var loc = b.Location is null ? "—" : MarkdownEscaping.CodeSpan(b.Location.ToString());
            md.AppendLine(
                $"| {MarkdownEscaping.TableCell(b.Name)} | {MarkdownEscaping.TableCell(b.ProjectName ?? "—")} | {loc} |");
        }
        md.AppendLine();
    }
}
