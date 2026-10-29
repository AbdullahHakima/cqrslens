namespace CqrsLens;

/// <summary>Connects notification handlers to the notifications they handle.</summary>
internal static class NotificationLinker
{
    public static void Link(AnalysisResult result)
    {
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
}
