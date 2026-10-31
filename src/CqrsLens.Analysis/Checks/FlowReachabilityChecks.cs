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
            result.Projects
                .Where(p => string.Equals(p.Layer, "Tests", StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Name),
            StringComparer.OrdinalIgnoreCase);

        var productionRequests = new HashSet<string>(StringComparer.Ordinal);
        var testRequests = new HashSet<string>(StringComparer.Ordinal);
        var productionNotifications = new HashSet<string>(StringComparer.Ordinal);
        var testNotifications = new HashSet<string>(StringComparer.Ordinal);

        foreach (var site in result.DispatchCallSites)
        {
            var production = site.TargetKind == DispatchTargetKind.Request
                ? productionRequests
                : productionNotifications;
            var tests = site.TargetKind == DispatchTargetKind.Request ? testRequests : testNotifications;
            (site.IsTestProject ? tests : production).Add(site.TargetFullName);
        }

        foreach (var endpoint in result.Endpoints)
        {
            if (string.IsNullOrEmpty(endpoint.TargetRequestFullName))
                continue;
            var isTest = endpoint.ProjectName is not null && testProjects.Contains(endpoint.ProjectName);
            (isTest ? testRequests : productionRequests).Add(endpoint.TargetRequestFullName);
        }

        var unresolved = result.UnresolvedDispatches.Count;
        var unresolvedNote = unresolved == 0
            ? ""
            : $" {unresolved} unresolved dispatch call(s) in the solution could not be resolved and are not counted as call sites.";

        foreach (var request in result.Requests)
        {
            if (request.Handlers.Count == 0 || productionRequests.Contains(request.FullName))
                continue;

            var testNote = testRequests.Contains(request.FullName)
                ? " Only test-project call site(s) were found."
                : "";
            findings.Add(new CheckFinding(
                CheckSeverity.Warning,
                ArchitectureChecker.UnreachableRequestCode,
                $"Request `{request.FullName}` has {request.Handlers.Count} handler(s) but no production call site."
                    + testNote + unresolvedNote,
                Target: request.FullName,
                Location: request.Location,
                ProjectName: request.ProjectName));
        }

        foreach (var notification in result.Notifications)
        {
            if (notification.Handlers.Count == 0 || productionNotifications.Contains(notification.FullName))
                continue;

            var testNote = testNotifications.Contains(notification.FullName)
                ? " Only test-project publisher(s) were found."
                : "";
            findings.Add(new CheckFinding(
                CheckSeverity.Warning,
                ArchitectureChecker.UnpublishedNotificationCode,
                $"Notification `{notification.FullName}` has {notification.Handlers.Count} handler(s) but no production publisher."
                    + testNote + unresolvedNote,
                Target: notification.FullName,
                Location: notification.Location,
                ProjectName: notification.ProjectName));
        }
    }
}
