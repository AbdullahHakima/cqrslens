namespace CqrsLens;

/// <summary>Connects notification handlers to the notifications they handle.</summary>
internal static class NotificationLinker
{
    public static void Link(AnalysisResult result)
    {
        MaterializeHandlerNotifications(result);

        var index = new LinkIndex<CqrsNotification>(
            result.Notifications,
            notification => notification.FullName,
            notification => notification.Name,
            notification => notification.ProjectName);

        foreach (var handler in result.NotificationHandlers.OrderBy(h => h.Name, StringComparer.OrdinalIgnoreCase))
        {
            var notification = index.Resolve(
                handler.NotificationName,
                handler.NotificationNamespace,
                handler.Namespace,
                handler.ImportedNamespaces,
                handler.ProjectName);
            if (notification is null)
            {
                LinkDiagnostics.RecordAmbiguous(
                    result,
                    "NotificationHandler",
                    handler.FullName,
                    handler.NotificationName,
                    handler.Location,
                    handler.ProjectName,
                    index.SimpleMatches(handler.NotificationName)
                        .Select(match => match.FullName)
                        .ToList());
                continue;
            }

            handler.NotificationNamespace ??= notification.Namespace;
            if (!notification.Handlers.Contains(handler.FullName))
                notification.Handlers.Add(handler.FullName);
        }

        foreach (var notification in result.Notifications)
            notification.Handlers.Sort(StringComparer.OrdinalIgnoreCase);

        result.Notifications.Sort((a, b) =>
        {
            var byName = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            return byName != 0 ? byName : string.Compare(a.FullName, b.FullName, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// Some mediator shapes (custom mediator packages, plain event records) declare
    /// notification handlers against event types that do not implement
    /// INotification or IDomainEvent. Those events are never discovered as
    /// notifications, so the model claims a handler exists for an event that does
    /// not. A notification handler's generic argument is a notification by
    /// contract, so inventory it here when it is not already present. Matching is by
    /// simple name, so an event that did implement a marker is never double counted
    /// and ambiguity is left to the linker.
    /// </summary>
    private static void MaterializeHandlerNotifications(AnalysisResult result)
    {
        if (result.NotificationHandlers.Count == 0)
            return;

        var knownNames = new HashSet<string>(
            result.Notifications.Select(notification => notification.Name),
            StringComparer.Ordinal);
        var materialized = new List<CqrsNotification>();

        foreach (var handler in result.NotificationHandlers)
        {
            var name = handler.NotificationName;
            if (string.IsNullOrEmpty(name) || !knownNames.Add(name))
                continue;

            materialized.Add(new CqrsNotification
            {
                Name = name,
                Namespace = handler.NotificationNamespace ?? handler.Namespace,
                ProjectName = handler.ProjectName,
                Location = handler.Location,
            });
        }

        result.Notifications.AddRange(materialized);
    }
}
