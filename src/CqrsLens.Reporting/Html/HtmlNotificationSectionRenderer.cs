using System.Text;
using static CqrsLens.HtmlReportText;

namespace CqrsLens;

/// <summary>Renders the domain events and notification handlers section.</summary>
internal static class HtmlNotificationSectionRenderer
{
    internal static void Render(
        StringBuilder html,
        AnalysisResult result,
        DiagramRenderMode renderMode)
    {
        if (result.Notifications.Count == 0 && result.NotificationHandlers.Count == 0)
            return;

        html.AppendLine("<h2>Domain Events &amp; Notifications</h2>");
        var notificationMap = DiagramBuilder.NotificationMap(result);
        if (notificationMap is not null)
        {
            if (!DiagramBuilder.IsWithinMermaidLimits(notificationMap))
            {
                html.AppendLine("<p class=\"note\"><em>Diagram omitted: notification graph exceeds the " +
                                "CqrsLens safety limit (45,000 characters / 400 edges).</em></p>");
            }
            else
            {
                html.AppendLine(HtmlDiagramSectionRenderer.Render(notificationMap, renderMode));
            }
        }

        if (result.Notifications.Count > 0)
        {
            html.AppendLine("<table>");
            html.AppendLine("<thead><tr><th>Notification</th><th>Marker</th><th>Handlers</th><th>Project</th><th>Source</th></tr></thead>");
            html.AppendLine("<tbody>");
            foreach (var n in result.Notifications)
            {
                var handlers = n.Handlers.Count > 0
                    ? string.Join(", ", n.Handlers.Select(CSharpScanner.SimpleName))
                    : "—";
                var loc = n.Location is null ? "—" : $"<code>{Encode(n.Location.ToString())}</code>";
                html.AppendLine(
                    $"<tr><td><code>{Encode(n.Name)}</code></td><td><code>{Encode(n.ViaMarker ?? "INotification")}</code></td>" +
                    $"<td>{Encode(handlers)}</td><td>{Encode(n.ProjectName ?? "—")}</td><td>{loc}</td></tr>");
            }
            html.AppendLine("</tbody>");
            html.AppendLine("</table>");
        }
    }
}