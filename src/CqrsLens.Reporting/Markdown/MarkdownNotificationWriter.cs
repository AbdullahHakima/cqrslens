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
            MarkdownDiagramWriter.AppendDiagramOrLimitNote(md, notificationMap, "notification map");
            md.AppendLine();
        }

        if (result.Notifications.Count > 0)
        {
            md.AppendLine("### Notifications");
            md.AppendLine();
            md.AppendLine("| Notification | Marker | Handlers | Project |");
            md.AppendLine("| --- | --- | --- | --- |");
            foreach (var n in result.Notifications)
            {
                md.AppendLine(
                    $"| {MarkdownEscaping.TableCell(n.Name)} | {MarkdownEscaping.TableCell(n.ViaMarker ?? "INotification")} | " +
                    $"{MarkdownEscaping.TableCell(n.Handlers.Count > 0 ? string.Join(", ", n.Handlers.Select(CSharpScanner.SimpleName)) : "—")} | " +
                    $"{MarkdownEscaping.TableCell(n.ProjectName ?? "—")} |");
            }
        }
        md.AppendLine();
    }
}
