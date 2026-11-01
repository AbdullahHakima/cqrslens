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
            AppendNotificationLegend(html);
            if (!DiagramBuilder.IsWithinMermaidLimits(notificationMap))
            {
                html.AppendLine("<p class=\"note\"><em>Diagram omitted: notification graph exceeds the " +
                                "CqrsLens safety limit (45,000 characters / 400 edges).</em></p>");
            }
            else
            {
                html.AppendLine("<figure class=\"diagram\">");
                html.AppendLine(HtmlDiagramSectionRenderer.Render(notificationMap, renderMode));
                html.AppendLine($"<figcaption>{Encode(NotificationCaption(result))}</figcaption>");
                html.AppendLine("</figure>");
            }
        }

        if (result.Notifications.Count > 0)
        {
            var allHandlers = result.Notifications
                .SelectMany(n => n.Handlers)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            html.AppendLine("<table>");
            html.AppendLine("<thead><tr><th>Notification</th><th>Marker</th><th>Handlers</th><th>Project</th><th>Source</th></tr></thead>");
            html.AppendLine("<tbody>");
            foreach (var n in result.Notifications)
            {
                var handlers = n.Handlers.Count > 0
                    ? string.Join(", ", n.Handlers
                        .Select(h => MermaidDiagramText.ShortTypeName(h, allHandlers))
                        .OrderBy(h => h, StringComparer.Ordinal))
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

    private static void AppendNotificationLegend(StringBuilder html)
    {
        html.AppendLine("<ul class=\"legend\" aria-label=\"Notification map legend\">");
        html.AppendLine("<li><span class=\"swatch\" style=\"background:#fdf4ff;border-color:#a21caf\" aria-hidden=\"true\"></span>Notification</li>");
        html.AppendLine("<li><span class=\"swatch\" style=\"background:#fffbeb;border-color:#d97706;border-style:dashed\" aria-hidden=\"true\"></span>Orphan notification</li>");
        html.AppendLine("<li><span class=\"swatch handler\" aria-hidden=\"true\"></span>Handler</li>");
        html.AppendLine("<li><span class=\"swatch\" style=\"background:#eef2ff;border-color:#4f46e5\" aria-hidden=\"true\"></span>Publisher</li>");
        html.AppendLine("</ul>");
    }

    private static string NotificationCaption(AnalysisResult result)
    {
        var caption = $"{MermaidDiagramText.CountText(result.Notifications.Count, "notification")}, " +
                      $"{MermaidDiagramText.CountText(result.NotificationHandlers.Count, "handler")}.";
        var omission = NotificationDiagramBuilder.ComputeOmission(result);
        if (omission.HasOmission)
        {
            caption += $" {omission.Omitted} notifications with no publisher or handler are omitted from " +
                       $"this diagram; all {omission.Total} are listed in the table below.";
        }
        return caption;
    }
}