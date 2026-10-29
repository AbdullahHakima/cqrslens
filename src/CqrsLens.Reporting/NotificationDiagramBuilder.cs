using System.Text;
using static CqrsLens.MermaidDiagramText;

namespace CqrsLens;

/// <summary>Builds the domain-event and notification fan-out diagram.</summary>
internal static class NotificationDiagramBuilder
{
    internal static string? NotificationMap(AnalysisResult result)
    {
        if (result.Notifications.Count == 0)
            return null;

        var orphanNotifications = result.Checks
            .Where(c => c.Code == ArchitectureChecker.OrphanNotificationCode && c.Target is not null)
            .Select(c => c.Target!)
            .ToHashSet(StringComparer.Ordinal);

        var groups = result.Notifications
            .GroupBy(n => string.IsNullOrEmpty(n.ProjectName) ? UnassignedProject : n.ProjectName, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new
            {
                Project = g.Key,
                Notifications = g.OrderBy(n => n.FullName, StringComparer.Ordinal).ToList(),
            })
            .ToList();
        var orderedNotifications = groups.SelectMany(g => g.Notifications).ToList();
        var nameCounts = orderedNotifications
            .GroupBy(n => n.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var handlerFqns = orderedNotifications
            .SelectMany(n => n.Handlers)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(h => h, StringComparer.Ordinal)
            .ToList();
        var handlerCounts = handlerFqns
            .GroupBy(CSharpScanner.SimpleName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var ids = new MermaidIdAllocator();
        var projectIds = new Dictionary<string, string>(StringComparer.Ordinal);

        var description =
            $"{CountText(orderedNotifications.Count, "notification")}, " +
            $"{CountText(handlerFqns.Count, "handler")}";
        if (orphanNotifications.Count > 0)
            description += $", {orphanNotifications.Count} OrphanNotification";
        description += ".";

        var sb = new StringBuilder();
        sb.AppendLine("flowchart LR");
        sb.AppendLine("    accTitle: Domain events and notifications map");
        sb.AppendLine($"    accDescr: {description}");

        foreach (var group in groups)
        {
            var projectId = ids.Allocate("notification-project:" + group.Project, "prj", group.Project);
            projectIds[group.Project] = projectId;
            var subgraphLabel = group.Project == UnassignedProject
                ? "Unassigned notifications"
                : $"Notifications in {group.Project}";
            sb.AppendLine($"    subgraph {projectId}[\"{MermaidLabel(subgraphLabel)}\"]");
            foreach (var notification in group.Notifications)
            {
                var label = nameCounts[notification.Name] > 1 ? notification.FullName : notification.Name;
                var nodeId = ids.Allocate("notification:" + notification.FullName, "ntf", label);
                var cls = orphanNotifications.Contains(notification.FullName)
                    ? "clsOrphanNotification"
                    : "clsNotification";
                sb.AppendLine($"        {nodeId}[\"{MermaidLabel(label)}\"]:::{cls}");
            }
            sb.AppendLine("    end");
        }

        var handlerIds = handlerFqns.ToDictionary(
            h => h,
            h => ids.Allocate(
                "notification-handler:" + h,
                "nhd",
                handlerCounts[CSharpScanner.SimpleName(h)] > 1
                    ? h
                    : CSharpScanner.SimpleName(h)),
            StringComparer.Ordinal);
        foreach (var handler in handlerFqns)
        {
            var simple = CSharpScanner.SimpleName(handler);
            var label = handlerCounts[simple] > 1 ? handler : simple;
            sb.AppendLine($"    {handlerIds[handler]}([\"{MermaidLabel(label)}\"]):::clsHandler");
        }

        foreach (var notification in orderedNotifications)
        {
            var label = nameCounts[notification.Name] > 1 ? notification.FullName : notification.Name;
            var nodeId = ids.Allocate("notification:" + notification.FullName, "ntf", label);
            foreach (var handler in notification.Handlers
                         .Distinct(StringComparer.Ordinal)
                         .OrderBy(h => h, StringComparer.Ordinal))
            {
                sb.AppendLine($"    {nodeId} -->|publishes to| {handlerIds[handler]}");
            }
        }

        foreach (var projectId in projectIds.Values)
            sb.AppendLine($"    style {projectId} fill:none,stroke:#94a3b8,stroke-dasharray:4 3");
        sb.AppendLine("    classDef clsNotification fill:#fdf4ff,stroke:#a21caf,stroke-width:2px,color:#4a044e");
        sb.AppendLine("    classDef clsHandler fill:#f8fafc,stroke:#475569,stroke-width:1.5px,color:#1e293b");
        if (orphanNotifications.Count > 0)
            sb.AppendLine("    classDef clsOrphanNotification fill:#fffbeb,stroke:#d97706,stroke-width:2px,stroke-dasharray:4 3,color:#78350f");

        return sb.ToString().TrimEnd();
    }
}