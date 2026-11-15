namespace CqrsLens;

/// <summary>
/// Dead-flow signals computed from resolved dispatch call sites:
/// <list type="bullet">
/// <item>a request that has handlers but no production call site;</item>
/// <item>a notification that has handlers but no production publisher.</item>
/// </list>
/// Both are warnings only. A call site in a test project never counts as production
/// reachability, and dispatches whose target type could not be resolved are reported
/// as an explicit population so a shape with no resolved call site is not silently
/// branded dead.
/// </summary>
internal static class FlowReachabilityChecks
{
    internal static void Run(AnalysisResult result, List<CheckFinding> findings)
    {
        if (!result.FlowAnalyzed || (result.Requests.Count == 0 && result.Notifications.Count == 0))
            return;

        var testProjects = new HashSet<string>(
            result.Spine.GetProjectsInLayer("Tests").Select(p => p.Name),
            StringComparer.OrdinalIgnoreCase);

        var productionRequests = new HashSet<string>(StringComparer.Ordinal);
        var testRequests = new HashSet<string>(StringComparer.Ordinal);
        var productionNotifications = new HashSet<string>(StringComparer.Ordinal);
        var testNotifications = new HashSet<string>(StringComparer.Ordinal);

        foreach (var rel in result.Spine.UnindexedRelations)
        {
            if (rel.Kind == RelationKind.Dispatches)
            {
                var targetFullName = rel.To.SymbolId?.FullName ?? rel.To.Name;
                foreach (var occ in rel.Occurrences)
                {
                    if (occ.Rule == EvidenceRule.DispatchCallSite)
                    {
                        (occ.IsTestProject ? testRequests : productionRequests).Add(targetFullName);
                    }
                    else if (occ.Rule == EvidenceRule.EndpointDispatch && rel.From.Kind == NodeReferenceKind.Endpoint)
                    {
                        var isTest = occ.ScopeProjectName is not null && testProjects.Contains(occ.ScopeProjectName);
                        (isTest ? testRequests : productionRequests).Add(targetFullName);
                    }
                }
            }
            else if (rel.Kind == RelationKind.Publishes)
            {
                var targetFullName = rel.To.SymbolId?.FullName ?? rel.To.Name;
                foreach (var occ in rel.Occurrences)
                {
                    if (occ.Rule == EvidenceRule.PublishCallSite)
                        (occ.IsTestProject ? testNotifications : productionNotifications).Add(targetFullName);
                }
            }
        }

        var unresolved = result.Spine.Unproven.UnresolvedDispatches.Count;
        var unresolvedNote = unresolved == 0
            ? ""
            : $" {unresolved} unresolved dispatch call(s) in the solution could not be resolved and are not counted as call sites.";

        foreach (var request in result.Requests)
        {
            var reqId = request.SpineId ?? result.IdentityResolver.ResolveComponentId(
                request.ProjectName, request.Namespace, request.ContainingTypes, request.Name);
            var handlerCount = result.Spine.CountOutgoingRelations(
                NodeReference.ForComponent(reqId), RelationKind.Serves);
            if (handlerCount == 0 || productionRequests.Contains(request.FullName))
                continue;

            var testNote = testRequests.Contains(request.FullName)
                ? " Only test-project call site(s) were found."
                : "";
            findings.Add(new CheckFinding(
                CheckSeverity.Warning,
                ArchitectureChecker.UnreachableRequestCode,
                $"Request `{request.FullName}` has {handlerCount} handler(s) but no production call site."
                    + testNote + unresolvedNote,
                Target: request.FullName,
                Location: request.Location,
                ProjectName: request.ProjectName));
        }

        foreach (var notification in result.Notifications)
        {
            var notifId = notification.SpineId ?? result.IdentityResolver.ResolveComponentId(
                notification.ProjectName, notification.Namespace, notification.ContainingTypes, notification.Name);
            var handlerCount = result.Spine.CountOutgoingRelations(
                NodeReference.ForComponent(notifId), RelationKind.HandlesNotification);
            if (handlerCount == 0 || productionNotifications.Contains(notification.FullName))
                continue;

            var testNote = testNotifications.Contains(notification.FullName)
                ? " Only test-project publisher(s) were found."
                : "";
            findings.Add(new CheckFinding(
                CheckSeverity.Warning,
                ArchitectureChecker.UnpublishedNotificationCode,
                $"Notification `{notification.FullName}` has {handlerCount} handler(s) but no production publisher."
                    + testNote + unresolvedNote,
                Target: notification.FullName,
                Location: notification.Location,
                ProjectName: notification.ProjectName));
        }
    }
}
