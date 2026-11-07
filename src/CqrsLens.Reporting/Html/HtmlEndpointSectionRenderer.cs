using System.Text;
using static CqrsLens.HtmlReportText;

namespace CqrsLens;

/// <summary>
/// Renders the endpoint catalog: one row per discovered HTTP endpoint. Pure HTML, so
/// it renders identically in the interactive and zero-JS SVG reports. The empty
/// state is explicit — the section never silently vanishes.
/// </summary>
internal static class HtmlEndpointSectionRenderer
{
    internal static void Append(StringBuilder html, AnalysisResult result)
    {
        var view = EndpointCatalogView.Build(result);

        html.AppendLine("<h2>API Endpoints</h2>");
        html.AppendLine("<p class=\"note\">The HTTP surface and the request each endpoint dispatches to. " +
                        "<code>inferred</code> marks a route or verb guessed from naming rather than read from " +
                        "source; an <code>unresolved</code> target has no matching request in the model.</p>");

        if (view.EndpointCount == 0)
        {
            html.AppendLine("<p><em>No endpoints discovered.</em></p>");
            return;
        }

        var inferred = view.InferredCount > 0
            ? $" ({view.InferredCount} inferred)"
            : "";
        html.AppendLine($"<p><strong>{view.EndpointCount} endpoint(s)</strong>{inferred} across " +
                        $"{view.ProjectCount} project(s).</p>");
        html.AppendLine("<table><thead><tr><th>Method</th><th>Route</th><th>Endpoint</th><th>Project</th>" +
                        "<th>Target request</th><th>Handler</th><th>Inferred</th></tr></thead><tbody>");
        foreach (var row in view.Rows)
            html.AppendLine($"<tr><td>{Encode(row.Method)}</td><td><code>{Encode(row.Route)}</code></td>" +
                            $"<td>{Encode(row.Endpoint)}</td><td>{Encode(row.Project)}</td>" +
                            $"<td>{Encode(row.Target)}</td><td>{Encode(row.Handler)}</td>" +
                            $"<td>{(row.Inferred ? "yes" : "—")}</td></tr>");
        html.AppendLine("</tbody></table>");
    }
}
