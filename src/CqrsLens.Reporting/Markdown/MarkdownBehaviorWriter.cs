using System.Text;

namespace CqrsLens;

/// <summary>Writes the pipeline behaviors registered-order view and chain diagram.</summary>
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

        md.AppendLine($"_{BehaviorPresentationText.RegisteredOrderNote}_");
        md.AppendLine();

        var chain = DiagramBuilder.BehaviorChain(result);
        if (chain is not null)
        {
            MarkdownDiagramWriter.AppendDiagramOrLimitNote(md, chain, "behavior chain");
            md.AppendLine();
        }

        var registered = BehaviorOrdering.Registered(result.Behaviors);
        if (registered.Count > 0)
        {
            md.AppendLine("### Registered order");
            md.AppendLine();
            foreach (var group in registered)
            {
                md.AppendLine($"#### {MarkdownEscaping.CodeSpan(group.Site)}");
                md.AppendLine();
                md.AppendLine("| # | Behavior | Project | Source |");
                md.AppendLine("| --- | --- | --- | --- |");
                foreach (var b in group.Behaviors)
                {
                    var loc = b.Location is null ? "—" : MarkdownEscaping.CodeSpan(b.Location.ToString());
                    md.AppendLine(
                        $"| {b.RegistrationOrder} | {MarkdownEscaping.TableCell(b.Name)} | " +
                        $"{MarkdownEscaping.TableCell(b.ProjectName ?? "—")} | {loc} |");
                }
                md.AppendLine();
            }
        }

        var unregistered = BehaviorOrdering.Unregistered(result.Behaviors);
        if (unregistered.Count > 0)
        {
            md.AppendLine($"### {BehaviorPresentationText.UnregisteredHeading}");
            md.AppendLine();
            md.AppendLine("| Behavior | Project | Source |");
            md.AppendLine("| --- | --- | --- |");
            foreach (var b in unregistered)
            {
                var loc = b.Location is null ? "—" : MarkdownEscaping.CodeSpan(b.Location.ToString());
                md.AppendLine(
                    $"| {MarkdownEscaping.TableCell(b.Name)} | {MarkdownEscaping.TableCell(b.ProjectName ?? "—")} | {loc} |");
            }
            md.AppendLine();
        }
    }
}
