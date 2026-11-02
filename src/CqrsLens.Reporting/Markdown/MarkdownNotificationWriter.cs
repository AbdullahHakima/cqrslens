using System.Text;

namespace CqrsLens;

/// <summary>Writes the domain events and notifications section.</summary>
internal static class MarkdownNotificationWriter
{
    internal static void Write(StringBuilder md, AnalysisResult result)
    {
        if (result.Notifications.Count == 0 && result.NotificationHandlers.Count == 0)
            return;

        md.AppendLine("## Domain Events & Notifications");
        md.AppendLine();
        var notificationMap = DiagramBuilder.NotificationMap(result);
        if (notificationMap is not null)
        {
            md.AppendLine(NotificationLegend());
            md.AppendLine();
            MarkdownDiagramWriter.AppendDiagramOrLimitNote(md, notificationMap, "notification map");
            md.AppendLine();
            var omission = NotificationDiagramBuilder.ComputeOmission(result);
            if (omission.HasOmission)
            {
                md.AppendLine($"_{omission.Omitted} notifications with no publisher or handler are omitted from " +
                              $"this diagram; all {omission.Total} are listed in the table below._");
                md.AppendLine();
            }
        }

        if (result.Notifications.Count > 0)
        {
            var allHandlers = result.Notifications
                .SelectMany(n => n.Handlers)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            md.AppendLine("### Notifications");
            md.AppendLine();
            md.AppendLine("| Notification | Marker | Handlers | Project | Source |");
            md.AppendLine("| --- | --- | --- | --- | --- |");
            foreach (var n in result.Notifications)
            {
                var handlers = n.Handlers.Count > 0
                    ? string.Join(", ", n.Handlers
                        .Select(h => MermaidDiagramText.ShortTypeName(h, allHandlers))
                        .OrderBy(h => h, StringComparer.Ordinal))
                    : "—";
                var source = n.Location is null ? "—" : MarkdownEscaping.CodeSpan(n.Location.ToString());
                md.AppendLine(
                    $"| {MarkdownEscaping.TableCell(n.Name)} | {MarkdownEscaping.TableCell(n.ViaMarker ?? "INotification")} | " +
                    $"{MarkdownEscaping.TableCell(handlers)} | " +
                    $"{MarkdownEscaping.TableCell(n.ProjectName ?? "—")} | {source} |");
            }
        }
        md.AppendLine();
    }

    private static string NotificationLegend()
        => "**Legend:** fuchsia = notification · amber dashed = orphan notification (`OrphanNotification` finding) · " +
           "gray stadium = handler · indigo parallelogram = publisher.";
}
