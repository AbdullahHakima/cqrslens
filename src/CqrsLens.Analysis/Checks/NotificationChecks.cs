namespace CqrsLens;

/// <summary>
/// Reports notifications and domain events that have no handler.
/// </summary>
internal static class NotificationChecks
{
    internal static void Run(AnalysisResult result, List<CheckFinding> findings)
    {
        foreach (var notification in result.Notifications)
        {
            var notifId = notification.SpineId ?? result.IdentityResolver.ResolveComponentId(
                notification.ProjectName, notification.Namespace, notification.ContainingTypes, notification.Name);
            var hasHandlers = result.Spine.HasOutgoingRelation(
                NodeReference.ForComponent(notifId), RelationKind.HandlesNotification);
            if (hasHandlers)
                continue;

            findings.Add(new CheckFinding(
                CheckSeverity.Warning,
                ArchitectureChecker.OrphanNotificationCode,
                $"Notification `{notification.FullName}` has no handler.",
                Target: notification.FullName,
                Location: notification.Location,
                ProjectName: notification.ProjectName));
        }
    }
}
