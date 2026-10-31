namespace CqrsLens;

/// <summary>
/// Builds the solution-wide flow graph: resolved dispatch call sites, the fixed
/// structural edges (endpoint → request, request → handler, handler → DbContext) and
/// the handler → request / handler → notification edges read from handler bodies.
/// Runs after deep-slice linking so the shape indexes are complete.
/// </summary>
internal static class FlowAnalyzer
{
    public static void Analyze(
        List<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles,
        AnalysisResult result)
    {
        if (result.Requests.Count == 0 && result.Notifications.Count == 0)
            return;

        result.FlowAnalyzed = true;

        var testProjects = new HashSet<string>(
            result.Projects
                .Where(p => string.Equals(p.Layer, "Tests", StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Name),
            StringComparer.OrdinalIgnoreCase);

        var declaredClasses = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (_, scan, _) in parsedFiles)
        {
            foreach (var decl in scan.Classes)
                declaredClasses.Add(decl.Name);
        }

        var requestIndex = new LinkIndex<CqrsRequest>(
            result.Requests, r => r.FullName, r => r.Name, r => r.ProjectName);
        var notificationIndex = new LinkIndex<CqrsNotification>(
            result.Notifications, n => n.FullName, n => n.Name, n => n.ProjectName);

        var handlerNames = new HashSet<string>(
            result.Handlers.Select(h => h.FullName), StringComparer.Ordinal);
        var notificationHandlerNames = new HashSet<string>(
            result.NotificationHandlers.Select(h => h.FullName), StringComparer.Ordinal);

        var scanner = new DispatchCallSiteScanner(
            result, requestIndex, notificationIndex, declaredClasses,
            handlerNames, notificationHandlerNames, testProjects);

        foreach (var (file, scan, mask) in parsedFiles)
        {
            if (!ContainsDispatchName(file.Code))
                continue;
            scanner.ScanFile(file, scan, mask);
        }

        AddEndpointToRequestEdges(result);
        AddRequestToHandlerEdges(result);
        AddHandlerToDbContextEdges(result, parsedFiles);

        SortAndDedupe(result);
    }

    private static bool ContainsDispatchName(string code)
        => code.Contains(".Send", StringComparison.Ordinal) ||
           code.Contains(".Publish", StringComparison.Ordinal) ||
           code.Contains(".InvokeAsync", StringComparison.Ordinal);

    private static void AddEndpointToRequestEdges(AnalysisResult result)
    {
        foreach (var endpoint in result.Endpoints)
        {
            if (string.IsNullOrEmpty(endpoint.TargetRequestFullName))
                continue;

            result.FlowEdges.Add(new FlowEdge
            {
                Kind = FlowEdgeKind.EndpointToRequest,
                From = endpoint.Name,
                To = endpoint.TargetRequestFullName,
                ProjectName = endpoint.ProjectName,
                Location = endpoint.Location,
            });
        }
    }

    private static void AddRequestToHandlerEdges(AnalysisResult result)
    {
        foreach (var request in result.Requests)
        {
            foreach (var handler in request.Handlers)
            {
                result.FlowEdges.Add(new FlowEdge
                {
                    Kind = FlowEdgeKind.RequestToHandler,
                    From = request.FullName,
                    To = handler,
                    ProjectName = request.ProjectName,
                    Location = request.Location,
                });
            }
        }
    }

    private static void AddHandlerToDbContextEdges(
        AnalysisResult result,
        List<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles)
    {
        if (result.DbContexts.Count == 0 ||
            (result.Handlers.Count == 0 && result.NotificationHandlers.Count == 0))
            return;

        // A duplicated simple name cannot be attributed to one context, so skip it.
        var contextsBySimple = result.DbContexts
            .GroupBy(c => c.Name, StringComparer.Ordinal)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single(), StringComparer.Ordinal);
        if (contextsBySimple.Count == 0)
            return;

        var dependencyOwners = new List<(string Owner, IEnumerable<string> Dependencies)>();
        foreach (var handler in result.Handlers)
            dependencyOwners.Add((handler.FullName, handler.Dependencies));
        foreach (var handler in result.NotificationHandlers)
            dependencyOwners.Add((handler.FullName, handler.Dependencies));

        var byOwner = dependencyOwners
            .GroupBy(o => o.Owner, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Dependencies, StringComparer.Ordinal);

        var bodyByOwner = new Dictionary<string, (string Code, SourceMask Mask, int Start, int End)>(StringComparer.Ordinal);
        foreach (var (file, scan, mask) in parsedFiles)
        {
            foreach (var decl in scan.Classes)
            {
                if (!byOwner.ContainsKey(decl.FullName) || decl.BodySpan.End <= decl.BodySpan.Start)
                    continue;
                bodyByOwner[decl.FullName] = (file.Code, mask, decl.BodySpan.Start, Math.Min(file.Code.Length, decl.BodySpan.End));
            }
        }

        foreach (var owner in byOwner.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var dependencies = byOwner[owner];
            var matched = new HashSet<string>(StringComparer.Ordinal);
            foreach (var dependency in dependencies)
            {
                var simple = CSharpScanner.UnqualifiedTypeName(dependency);
                if (contextsBySimple.ContainsKey(simple))
                    matched.Add(simple);
            }

            if (bodyByOwner.TryGetValue(owner, out var body))
            {
                foreach (var simple in contextsBySimple.Keys)
                {
                    if (matched.Contains(simple))
                        continue;
                    if (BodyReferencesType(body.Code, body.Mask, body.Start, body.End, simple))
                        matched.Add(simple);
                }
            }

            foreach (var simple in matched.OrderBy(s => s, StringComparer.Ordinal))
            {
                result.FlowEdges.Add(new FlowEdge
                {
                    Kind = FlowEdgeKind.HandlerToDbContext,
                    From = owner,
                    To = contextsBySimple[simple].FullName,
                    ProjectName = contextsBySimple[simple].ProjectName,
                    Location = contextsBySimple[simple].Location,
                });
            }
        }
    }

    private static bool BodyReferencesType(string code, SourceMask mask, int start, int end, string name)
    {
        if (name.Length == 0)
            return false;

        var index = start;
        while (index < end)
        {
            index = code.IndexOf(name, index, StringComparison.Ordinal);
            if (index < 0 || index >= end)
                return false;

            var before = index == 0 ? '\0' : code[index - 1];
            var afterIndex = index + name.Length;
            var after = afterIndex >= code.Length ? '\0' : code[afterIndex];
            if (!IsIdentifierChar(before) && !IsIdentifierChar(after) && mask.IsCode(index))
                return true;

            index += name.Length;
        }

        return false;
    }

    private static bool IsIdentifierChar(char c)
        => char.IsLetterOrDigit(c) || c == '_';

    private static void SortAndDedupe(AnalysisResult result)
    {
        var edges = result.FlowEdges
            .GroupBy(e => (e.Kind, e.From, e.To))
            .Select(g => g.First())
            .OrderBy(e => e.Kind)
            .ThenBy(e => e.From, StringComparer.Ordinal)
            .ThenBy(e => e.To, StringComparer.Ordinal)
            .ToList();
        result.FlowEdges.Clear();
        result.FlowEdges.AddRange(edges);

        var sites = result.DispatchCallSites
            .GroupBy(s => (s.TargetFullName, s.TargetKind, s.SenderFullName, s.Location?.File, s.Location?.Line))
            .Select(g => g.First())
            .OrderBy(s => s.TargetKind)
            .ThenBy(s => s.TargetFullName, StringComparer.Ordinal)
            .ThenBy(s => s.SenderFullName, StringComparer.Ordinal)
            .ToList();
        result.DispatchCallSites.Clear();
        result.DispatchCallSites.AddRange(sites);

        var unresolved = result.UnresolvedDispatches
            .OrderBy(u => u.ProjectName, StringComparer.Ordinal)
            .ThenBy(u => u.Location?.File, StringComparer.Ordinal)
            .ThenBy(u => u.Location?.Line)
            .ThenBy(u => u.Method, StringComparer.Ordinal)
            .ToList();
        result.UnresolvedDispatches.Clear();
        result.UnresolvedDispatches.AddRange(unresolved);
    }
}
