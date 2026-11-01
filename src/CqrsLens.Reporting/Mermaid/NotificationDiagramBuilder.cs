using System.Text;
using static CqrsLens.MermaidDiagramText;

namespace CqrsLens;

/// <summary>Builds the domain-event and notification fan-out diagram.</summary>
internal static class NotificationDiagramBuilder
{
    /// <summary>
    /// Above this many notifications the diagram drops completely isolated
    /// notifications (no publisher and no handler) and states the omitted count in
    /// the caption. All notifications still appear in the table and in the checks.
    /// </summary>
    internal const int ReadableNotificationLimit = 40;

    /// <summary>How many isolated notifications the diagram omits, and the total.</summary>
    internal readonly record struct Omission(int Total, int Omitted)
    {
        internal bool HasOmission => Omitted > 0;
    }

    internal static Omission ComputeOmission(AnalysisResult result)
    {
        if (result.Notifications.Count <= ReadableNotificationLimit)
            return new Omission(result.Notifications.Count, 0);

        var published = result.DispatchCallSites
            .Where(s => s.TargetKind == DispatchTargetKind.Notification)
            .Select(s => s.TargetFullName)
            .ToHashSet(StringComparer.Ordinal);
        var omitted = result.Notifications.Count(n => n.Handlers.Count == 0 && !published.Contains(n.FullName));
        return new Omission(result.Notifications.Count, omitted);
    }

    internal static string? NotificationMap(AnalysisResult result)
    {
        if (result.Notifications.Count == 0)
            return null;

        var orphanNotifications = result.Checks
            .Where(c => c.Code == ArchitectureChecker.OrphanNotificationCode && c.Target is not null)
            .Select(c => c.Target!)
            .ToHashSet(StringComparer.Ordinal);

        var omission = ComputeOmission(result);
        var diagramNotifications = omission.HasOmission
            ? result.Notifications
                .Where(n => n.Handlers.Count > 0 ||
                            result.DispatchCallSites.Any(s =>
                                s.TargetKind == DispatchTargetKind.Notification &&
                                string.Equals(s.TargetFullName, n.FullName, StringComparison.Ordinal)))
                .ToList()
            : result.Notifications;

        var groups = diagramNotifications
            .GroupBy(n => string.IsNullOrEmpty(n.ProjectName) ? UnassignedProject : n.ProjectName, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new
            {
                Project = g.Key,
                Notifications = g.OrderBy(n => n.FullName, StringComparer.Ordinal).ToList(),
            })
            .ToList();
        var orderedNotifications = groups.SelectMany(g => g.Notifications).ToList();
        var notificationFqns = orderedNotifications.Select(n => n.FullName).ToList();
        var nameCounts = orderedNotifications
            .GroupBy(n => n.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var handlerFqns = orderedNotifications
            .SelectMany(n => n.Handlers)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(h => h, StringComparer.Ordinal)
            .ToList();

        // Publisher side (Phase 4): only resolved dispatch call sites are drawn; an
        // unresolved publish is never turned into a fabricated publisher edge.
        var notificationNames = orderedNotifications
            .Select(n => n.FullName)
            .ToHashSet(StringComparer.Ordinal);
        var publishersByNotification = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var site in result.DispatchCallSites
                     .Where(s => s.TargetKind == DispatchTargetKind.Notification &&
                                 s.SenderFullName is { Length: > 0 } &&
                                 notificationNames.Contains(s.TargetFullName))
                     .OrderBy(s => s.TargetFullName, StringComparer.Ordinal)
                     .ThenBy(s => s.SenderFullName, StringComparer.Ordinal))
        {
            if (!publishersByNotification.TryGetValue(site.TargetFullName, out var list))
                publishersByNotification[site.TargetFullName] = list = new List<string>();
            if (!list.Contains(site.SenderFullName!, StringComparer.Ordinal))
                list.Add(site.SenderFullName!);
        }

        var publisherFqns = publishersByNotification.Values
            .SelectMany(v => v)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        var ids = new MermaidIdAllocator();
        var projectIds = new Dictionary<string, string>(StringComparer.Ordinal);

        var description =
            $"{CountText(orderedNotifications.Count, "notification")}, " +
            $"{CountText(handlerFqns.Count, "handler")}";
        if (publisherFqns.Count > 0)
            description += $", {CountText(publisherFqns.Count, "publisher")}";
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
                var label = nameCounts[notification.Name] > 1
                    ? ShortTypeName(notification.FullName, notificationFqns)
                    : notification.Name;
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
                ShortTypeName(h, handlerFqns)),
            StringComparer.Ordinal);
        foreach (var handler in handlerFqns)
        {
            var label = ShortTypeName(handler, handlerFqns);
            sb.AppendLine($"    {handlerIds[handler]}([\"{MermaidLabel(label)}\"]):::clsHandler");
        }

        var publisherIds = publisherFqns.ToDictionary(
            p => p,
            p => ids.Allocate(
                "notification-publisher:" + p,
                "pub",
                ShortTypeName(p, publisherFqns)),
            StringComparer.Ordinal);
        foreach (var publisher in publisherFqns)
        {
            var label = ShortTypeName(publisher, publisherFqns);
            sb.AppendLine($"    {publisherIds[publisher]}[/\"{MermaidLabel(label)}\"/]:::clsPublisher");
        }

        foreach (var notification in orderedNotifications)
        {
            var label = nameCounts[notification.Name] > 1
                ? ShortTypeName(notification.FullName, notificationFqns)
                : notification.Name;
            var nodeId = ids.Allocate("notification:" + notification.FullName, "ntf", label);
            if (publishersByNotification.TryGetValue(notification.FullName, out var publishers))
            {
                foreach (var publisher in publishers.OrderBy(p => p, StringComparer.Ordinal))
                    sb.AppendLine($"    {publisherIds[publisher]} -->|publishes| {nodeId}");
            }
            foreach (var handler in notification.Handlers
                         .Distinct(StringComparer.Ordinal)
                         .OrderBy(h => h, StringComparer.Ordinal))
            {
                sb.AppendLine($"    {nodeId} -->|handled by| {handlerIds[handler]}");
            }
        }

        foreach (var projectId in projectIds.Values)
            sb.AppendLine($"    style {projectId} fill:none,stroke:#94a3b8,stroke-dasharray:4 3");
        sb.AppendLine("    classDef clsNotification fill:#fdf4ff,stroke:#a21caf,stroke-width:2px,color:#4a044e");
        sb.AppendLine("    classDef clsHandler fill:#f8fafc,stroke:#475569,stroke-width:1.5px,color:#1e293b");
        if (publisherFqns.Count > 0)
            sb.AppendLine("    classDef clsPublisher fill:#eef2ff,stroke:#4f46e5,stroke-width:2px,color:#312e81");
        if (orphanNotifications.Count > 0)
            sb.AppendLine("    classDef clsOrphanNotification fill:#fffbeb,stroke:#d97706,stroke-width:2px,stroke-dasharray:4 3,color:#78350f");

        return sb.ToString().TrimEnd();
    }
}