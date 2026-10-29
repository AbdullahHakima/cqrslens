using System.Text;
using static CqrsLens.HtmlReportText;

namespace CqrsLens;

/// <summary>Renders the pipeline behavior table.</summary>
internal static class HtmlBehaviorSectionRenderer
{
    internal static void Append(StringBuilder html, AnalysisResult result)
    {
        html.AppendLine("<h2>Pipeline Behaviors</h2>");
        if (result.Behaviors.Count == 0)
        {
            html.AppendLine("<p><em>No IPipelineBehavior implementations found.</em></p>");
        }
        else
        {
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
    }
}