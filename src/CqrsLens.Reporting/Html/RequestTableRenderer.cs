using System.Text;

namespace CqrsLens;

/// <summary>Renders the searchable requests table.</summary>
internal static class RequestTableRenderer
{
    internal static void Render(StringBuilder html, AnalysisResult result, RequestExplorerIndex index)
    {
        html.AppendLine("<table class=\"requests-table\">");
        html.AppendLine("<thead><tr><th>Request</th><th>Type</th><th>Response</th><th>Handler</th><th>Validators</th><th>Project</th></tr></thead>");
        html.AppendLine("<tbody>");
        foreach (var r in result.Requests)
        {
            var anchor = index.Anchors[r];
            var searchBlob = index.BuildSearchBlob(r);
            html.AppendLine($"<tr data-search=\"{HtmlReportText.Encode(searchBlob)}\">" +
                $"<td><a class=\"req-link\" href=\"#{HtmlReportText.Encode(anchor)}\">{HtmlReportText.Encode(r.Name)}</a></td>" +
                $"<td>{HtmlReportText.Encode(r.Kind)}</td>" +
                $"<td>{HtmlReportText.Encode(r.ResponseType ?? "—")}</td>" +
                $"<td>{HtmlReportText.Encode(r.Handlers.Count > 0 ? string.Join(", ", r.Handlers.Select(CSharpScanner.SimpleName)) : "—")}</td>" +
                $"<td>{HtmlReportText.Encode(r.Validators.Count > 0 ? string.Join(", ", r.Validators.Select(CSharpScanner.SimpleName)) : "—")}</td>" +
                $"<td>{HtmlReportText.Encode(r.ProjectName ?? "—")}</td>" +
                "</tr>");
        }
        html.AppendLine("</tbody>");
        html.AppendLine("</table>");
    }
}
