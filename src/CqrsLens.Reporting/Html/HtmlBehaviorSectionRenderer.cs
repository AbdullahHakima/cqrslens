using System.Text;
using static CqrsLens.HtmlReportText;

namespace CqrsLens;

/// <summary>Renders the pipeline behaviors registered-order view and chain diagram.</summary>
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

        html.AppendLine($"<p class=\"note\">{Encode(BehaviorPresentationText.RegisteredOrderNote)}</p>");

        var chain = DiagramBuilder.BehaviorChain(result);
        if (chain is not null && DiagramBuilder.IsWithinMermaidLimits(chain))
        {
            html.AppendLine("<figure class=\"diagram\">");
            html.AppendLine(HtmlDiagramSectionRenderer.Render(chain, renderMode));
            html.AppendLine($"<figcaption>{Encode(BehaviorPresentationText.ChainCaption(result.Behaviors))}</figcaption>");
            html.AppendLine("</figure>");
        }

        var registered = BehaviorOrdering.Registered(result.Behaviors);
        if (registered.Count > 0)
        {
            html.AppendLine("<h3>Registered order</h3>");
            foreach (var group in registered)
            {
                html.AppendLine($"<h4><code>{Encode(group.Site)}</code></h4>");
                html.AppendLine("<table><thead><tr><th>#</th><th>Behavior</th><th>Project</th><th>Source</th></tr></thead><tbody>");
                foreach (var b in group.Behaviors)
                {
                    var loc = b.Location is null ? "—" : $"<code>{Encode(b.Location.ToString())}</code>";
                    html.AppendLine(
                        $"<tr><td>{b.RegistrationOrder}</td><td>{Encode(b.Name)}</td>" +
                        $"<td>{Encode(b.ProjectName ?? "—")}</td><td>{loc}</td></tr>");
                }
                html.AppendLine("</tbody></table>");
            }
        }

        var unregistered = BehaviorOrdering.Unregistered(result.Behaviors);
        if (unregistered.Count > 0)
        {
            html.AppendLine($"<h3>{Encode(BehaviorPresentationText.UnregisteredHeading)}</h3>");
            html.AppendLine("<table><thead><tr><th>Behavior</th><th>Project</th><th>Source</th></tr></thead><tbody>");
            foreach (var b in unregistered)
            {
                var loc = b.Location is null ? "—" : $"<code>{Encode(b.Location.ToString())}</code>";
                html.AppendLine($"<tr><td>{Encode(b.Name)}</td><td>{Encode(b.ProjectName ?? "—")}</td><td>{loc}</td></tr>");
            }
            html.AppendLine("</tbody></table>");
        }
    }
}
