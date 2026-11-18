using System.Text;
using static CqrsLens.HtmlReportText;

namespace CqrsLens;

/// <summary>
/// Renders the unified Architecture Spine section in HTML reports, carrying identical
/// data and deterministic ordering to <see cref="MarkdownSpineWriter"/>.
/// </summary>
internal static class HtmlSpineSectionRenderer
{
    internal static void Append(StringBuilder html, AnalysisResult result)
    {
        var view = SpineReportView.Build(result);

        html.AppendLine("<h2>Architecture Spine</h2>");
        html.AppendLine("<p class=\"note\">Evidence-backed component, endpoint, and relation inventory from the unified architecture spine.</p>");

        html.AppendLine("<table><thead><tr><th>Metric</th><th>Count</th></tr></thead><tbody>");
        html.AppendLine($"<tr><td>Unique components</td><td>{view.ComponentCount}</td></tr>");
        html.AppendLine($"<tr><td>Endpoints (<code>spineEndpoints</code>)</td><td>{view.EndpointCount}</td></tr>");
        html.AppendLine($"<tr><td>Relations</td><td>{view.RelationCount}</td></tr>");
        html.AppendLine($"<tr><td>Relation evidence occurrences</td><td>{view.TotalRelationOccurrences}</td></tr>");
        html.AppendLine($"<tr><td>Unproven items</td><td>{view.UnprovenCount}</td></tr>");
        html.AppendLine("</tbody></table>");

        html.AppendLine("<h3>Components by Role</h3>");
        html.AppendLine($"<p class=\"note\">One declared symbol yields at most one component node with an evidenced role set (unique components: {view.ComponentCount}).</p>");
        html.AppendLine("<table><thead><tr><th>Role</th><th>Components</th><th>Role Evidence Occurrences</th></tr></thead><tbody>");
        foreach (var row in view.Roles)
        {
            var roleCell = row.IsMappingDerivedEndpoint
                ? $"<code>{Encode(row.RoleName)}</code> (mapping-derived)"
                : $"<code>{Encode(row.RoleName)}</code>";
            html.AppendLine($"<tr><td>{roleCell}</td><td>{row.ComponentCount}</td><td>{row.EvidenceOccurrences}</td></tr>");
        }
        html.AppendLine("</tbody></table>");

        html.AppendLine("<h3>Relations Summary</h3>");
        if (view.RelationsSummary.Count == 0)
        {
            html.AppendLine("<p><em>No relations recorded in the model.</em></p>");
        }
        else
        {
            html.AppendLine("<table><thead><tr><th>Relation Kind</th><th>Relations</th><th>Occurrences</th></tr></thead><tbody>");
            foreach (var row in view.RelationsSummary)
            {
                html.AppendLine($"<tr><td><code>{Encode(row.KindName)}</code></td><td>{row.RelationCount}</td><td>{row.OccurrenceCount}</td></tr>");
            }
            html.AppendLine($"<tr><td><strong>Total</strong></td><td><strong>{view.RelationCount}</strong></td><td><strong>{view.TotalRelationOccurrences}</strong></td></tr>");
            html.AppendLine("</tbody></table>");
        }

        html.AppendLine("<h3>Endpoint Inventory</h3>");
        if (view.Endpoints.Count == 0)
        {
            html.AppendLine("<p><em>No endpoints discovered in the model.</em></p>");
        }
        else
        {
            html.AppendLine("<table><thead><tr><th>Method</th><th>Route</th><th>Endpoint</th><th>Project</th>" +
                            "<th>Dispatches to</th><th>Inferred</th><th>Location</th></tr></thead><tbody>");
            foreach (var row in view.Endpoints)
            {
                var routeCell = string.IsNullOrEmpty(row.Route)
                    ? "—"
                    : $"<code>{Encode(row.Route)}</code>";
                var locationCell = FormatLocation(row.Location);
                html.AppendLine(
                    $"<tr><td>{Encode(row.Method)}</td><td>{routeCell}</td>" +
                    $"<td>{Encode(row.Endpoint)}</td><td>{Encode(row.Project)}</td>" +
                    $"<td>{Encode(row.DispatchesTo)}</td><td>{(row.Inferred ? "yes" : "—")}</td>" +
                    $"<td>{locationCell}</td></tr>");
            }
            html.AppendLine("</tbody></table>");
        }

        html.AppendLine("<h3>Component Index</h3>");
        if (view.Components.Count == 0)
        {
            html.AppendLine("<p><em>No components discovered in the model.</em></p>");
        }
        else
        {
            html.AppendLine("<table><thead><tr><th>Component</th><th>Roles</th><th>Project</th><th>Location</th></tr></thead><tbody>");
            foreach (var row in view.Components)
            {
                html.AppendLine(
                    $"<tr><td>{Encode(row.Component)}</td><td>{Encode(row.Roles)}</td>" +
                    $"<td>{Encode(row.Project)}</td><td>{FormatLocation(row.Location)}</td></tr>");
            }
            html.AppendLine("</tbody></table>");
        }

        html.AppendLine("<h3>Most-Connected Components</h3>");
        if (view.MostConnected.Count == 0)
        {
            html.AppendLine("<p><em>No components discovered in the model.</em></p>");
        }
        else
        {
            html.AppendLine("<table><thead><tr><th>Component</th><th>In</th><th>Out</th><th>Total</th></tr></thead><tbody>");
            foreach (var row in view.MostConnected)
            {
                html.AppendLine(
                    $"<tr><td>{Encode(row.Component)}</td><td>{row.Incoming}</td>" +
                    $"<td>{row.Outgoing}</td><td>{row.Total}</td></tr>");
            }
            html.AppendLine("</tbody></table>");
        }
    }

    private static string FormatLocation(SourceLocation? location)
        => location is null ? "—" : $"<code>{Encode($"{location.File}:{location.Line}")}</code>";
}
