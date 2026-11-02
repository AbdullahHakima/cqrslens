using System.Text;
using static CqrsLens.HtmlReportText;

namespace CqrsLens;

/// <summary>Renders the pipeline behavior chain diagram and table.</summary>
internal static class HtmlBehaviorSectionRenderer
{
    internal static void Append(
        StringBuilder html,
        AnalysisResult result,
        DiagramRenderMode renderMode)
    {
        html.AppendLine("<h2>Pipeline Behaviors</h2>");
        if (result.Behaviors.Count == 0)
        {
            html.AppendLine("<p><em>No IPipelineBehavior implementations found.</em></p>");
            return;
        }

        var chain = DiagramBuilder.BehaviorChain(result);
        html.AppendLine("<p class=\"note\">Behaviors wrap every request that flows through the " +
                        "pipeline. The diagram lists them in the order the model recorded them " +
                        "(scan order, not resolved execution order).</p>");
        if (chain is not null && DiagramBuilder.IsWithinMermaidLimits(chain))
        {
            html.AppendLine("<figure class=\"diagram\">");
            html.AppendLine(HtmlDiagramSectionRenderer.Render(chain, renderMode));
            html.AppendLine($"<figcaption>{Encode(ChainCaption(result))}</figcaption>");
            html.AppendLine("</figure>");
        }

        html.AppendLine("<table>");
        html.AppendLine("<thead><tr><th>Behavior</th><th>Project</th><th>Source</th></tr></thead>");
        html.AppendLine("<tbody>");
        foreach (var b in result.Behaviors.OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase))
        {
            var loc = b.Location is null ? "—" : $"<code>{Encode(b.Location.ToString())}</code>";
            html.AppendLine($"<tr><td>{Encode(b.Name)}</td><td>{Encode(b.ProjectName ?? "—")}</td><td>{loc}</td></tr>");
        }
        html.AppendLine("</tbody>");
        html.AppendLine("</table>");
    }

    private static string ChainCaption(AnalysisResult result)
        => $"{MermaidDiagramText.CountText(result.Behaviors.Count, "behavior")} in scan order, not resolved execution order.";
}
