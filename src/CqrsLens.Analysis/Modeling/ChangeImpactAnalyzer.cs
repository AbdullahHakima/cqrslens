namespace CqrsLens;

/// <summary>
/// Traces bidirectional dependency impact across projects, CQRS slices, DI registrations,
/// service call edges, DbContexts, entities, and static test references.
/// </summary>
public static class ChangeImpactAnalyzer
{
    private static readonly string[] ValidTargetKinds =
    [
        "project", "entity", "dbcontext", "service", "notification", "handler", "request",
        "entrypoint", "event",
    ];

    public static ChangeImpactReport Analyze(
        AnalysisResult result,
        string target,
        string? targetKind = null,
        int maxDepth = 5)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        var clampedDepth = Math.Clamp(maxDepth, 1, 10);
        var query = target.Trim();
        var normalizedKind = NormalizeTargetKind(targetKind);
        var blindSpots = CollectBlindSpots(result);
        var allTestRefs = TestProjectDiscovery.FindAllTestReferences(result);

        var graph = ImpactGraph.Build(result);
        var matches = graph.FindTargets(query, normalizedKind);

        if (matches.Count == 0)
        {
            return new ChangeImpactReport(
                "not_found",
                "partial",
                clampedDepth,
                null,
                [],
                [],
                [],
                [],
                [],
                blindSpots);
        }

        if (matches.Count > 1)
        {
            var candidates = matches
                .Select(n => n.ToTarget())
                .OrderBy(t => t.Kind, StringComparer.Ordinal)
                .ThenBy(t => t.FullName ?? t.Name, StringComparer.Ordinal)
                .ToArray();

            return new ChangeImpactReport(
                "ambiguous",
                "partial",
                clampedDepth,
                null,
                candidates,
                [],
                [],
                [],
                [],
                blindSpots);
        }

        var rootNode = matches[0];
        var impacted = Traverse(graph, rootNode, clampedDepth);

        var impactedRequests = CollectImpactedRequests(result, rootNode, impacted);
        var impactedTargetNames = CollectTestTargetNames(result, rootNode, impacted, impactedRequests);

        var matchedTestRefs = allTestRefs
            .Where(tr => MatchesAnyTarget(tr.TargetFullName, impactedTargetNames))
            .ToList();

        var universalTargets = new List<(string TargetName, string? ProjectName)>();
        if (rootNode.Kind is "entrypoint" or "service" or "event")
            universalTargets.Add((rootNode.Kind == "entrypoint" ? rootNode.Name : rootNode.FullName, rootNode.ProjectName));
        foreach (var item in impacted)
        {
            if (item.Kind == "entrypoint")
                universalTargets.Add((item.Name, item.ProjectName));
        }

        if (universalTargets.Count > 0)
        {
            var seenRefs = new HashSet<TestMethodReference>(matchedTestRefs);
            foreach (var (targetName, projName) in universalTargets.Distinct())
            {
                if (string.IsNullOrWhiteSpace(targetName))
                    continue;
                foreach (var tr in TestProjectDiscovery.FindTestReferencesForTarget(result, targetName, projName))
                {
                    if (seenRefs.Add(tr))
                        matchedTestRefs.Add(tr);
                }
            }
        }

        var existingTestRefs = matchedTestRefs
            .OrderBy(tr => tr.ProjectName, StringComparer.Ordinal)
            .ThenBy(tr => tr.File, StringComparer.Ordinal)
            .ThenBy(tr => tr.Line)
            .ThenBy(tr => tr.ClassName, StringComparer.Ordinal)
            .ThenBy(tr => tr.MethodName, StringComparer.Ordinal)
            .ThenBy(tr => tr.TargetFullName, StringComparer.Ordinal)
            .ThenBy(tr => tr.TargetKind, StringComparer.Ordinal)
            .ThenBy(tr => tr.ReferenceKind, StringComparer.Ordinal)
            .ToArray();

        var uncoveredHotspots = CollectUncoveredHotspots(impactedRequests, allTestRefs);

        var affectedProjects = CollectAffectedProjects(rootNode, impacted);
        var confidence = blindSpots.Length > 0 ? "partial" : "high";

        return new ChangeImpactReport(
            "analyzed",
            confidence,
            clampedDepth,
            rootNode.ToTarget(),
            [rootNode.ToTarget()],
            impacted,
            affectedProjects,
            existingTestRefs,
            uncoveredHotspots,
            blindSpots);
    }

    private static string? NormalizeTargetKind(string? targetKind)
    {
        if (string.IsNullOrWhiteSpace(targetKind))
            return null;

        var normalized = targetKind.Trim().ToLowerInvariant();
        if (!ValidTargetKinds.Contains(normalized, StringComparer.Ordinal))
        {
            throw new ArgumentException(
                $"Unsupported targetKind '{targetKind}'. Expected one of: {string.Join(", ", ValidTargetKinds)}.",
                nameof(targetKind));
        }

        return normalized;
    }

    private static List<ChangeImpactItem> Traverse(
        ImpactGraph graph,
        ImpactNode root,
        int maxDepth)
    {
        var discovered = new Dictionary<string, ChangeImpactItem>(StringComparer.Ordinal);
        var reverseQueue = new Queue<(ImpactNode Node, int Depth, List<string> Path)>();
        var forwardQueue = new Queue<(ImpactNode Node, int Depth, List<string> Path)>();
        var reverseVisited = new HashSet<string>(StringComparer.Ordinal) { root.Id };
        var forwardVisited = new HashSet<string>(StringComparer.Ordinal) { root.Id };

        reverseQueue.Enqueue((root, 0, [root.DisplayPathLabel]));
        forwardQueue.Enqueue((root, 0, [root.DisplayPathLabel]));

        if (root.Kind == "project")
        {
            foreach (var owned in graph.GetProjectOwnedNodes(root))
            {
                if (!reverseVisited.Add(owned.Id))
                    continue;
                forwardVisited.Add(owned.Id);

                var path = new List<string> { root.DisplayPathLabel, owned.DisplayPathLabel };
                discovered.TryAdd(owned.Id, new ChangeImpactItem(
                    owned.Kind,
                    owned.Name,
                    owned.FullName,
                    owned.ProjectName,
                    "downstream",
                    1,
                    "declared in target project",
                    path,
                    owned.Location));

                if (maxDepth > 1)
                    reverseQueue.Enqueue((owned, 1, path));
            }
        }

        while (reverseQueue.TryDequeue(out var current))
        {
            if (current.Depth >= maxDepth)
                continue;

            foreach (var edge in graph.GetIncoming(current.Node))
            {
                var nextDepth = current.Depth + 1;
                var nextPath = new List<string>(current.Path.Count + 1) { edge.Source.DisplayPathLabel };
                nextPath.AddRange(current.Path);

                if (reverseVisited.Add(edge.Source.Id))
                {
                    RecordItem(discovered, edge.Source, "upstream", nextDepth, edge.Reason, nextPath);
                    reverseQueue.Enqueue((edge.Source, nextDepth, nextPath));
                }
            }
        }

        while (forwardQueue.TryDequeue(out var current))
        {
            if (current.Depth >= maxDepth)
                continue;

            foreach (var edge in graph.GetOutgoing(current.Node))
            {
                var nextDepth = current.Depth + 1;
                var nextPath = new List<string>(current.Path.Count + 1);
                nextPath.AddRange(current.Path);
                nextPath.Add(edge.Target.DisplayPathLabel);

                if (forwardVisited.Add(edge.Target.Id))
                {
                    RecordItem(discovered, edge.Target, "downstream", nextDepth, edge.Reason, nextPath);
                    forwardQueue.Enqueue((edge.Target, nextDepth, nextPath));
                }
            }
        }

        return discovered.Values
            .OrderBy(i => i.Depth)
            .ThenBy(i => i.Direction, StringComparer.Ordinal)
            .ThenBy(i => i.Kind, StringComparer.Ordinal)
            .ThenBy(i => i.FullName, StringComparer.Ordinal)
            .ToList();
    }

    private static void RecordItem(
        Dictionary<string, ChangeImpactItem> discovered,
        ImpactNode node,
        string direction,
        int depth,
        string reason,
        List<string> path)
    {
        var candidate = new ChangeImpactItem(
            node.Kind,
            node.Name,
            node.FullName,
            node.ProjectName,
            direction,
            depth,
            reason,
            path,
            node.Location);

        if (!discovered.TryGetValue(node.Id, out var existing) || depth < existing.Depth)
            discovered[node.Id] = candidate;
    }

    private static List<CqrsRequest> CollectImpactedRequests(
        AnalysisResult result,
        ImpactNode root,
        IReadOnlyList<ChangeImpactItem> impacted)
    {
        var fullNames = new HashSet<string>(StringComparer.Ordinal);
        if (root.Kind == "request")
            fullNames.Add(root.FullName);

        foreach (var item in impacted)
        {
            if (item.Kind == "request")
                fullNames.Add(item.FullName);
        }

        return result.Requests
            .Where(r => fullNames.Contains(r.FullName))
            .OrderBy(r => r.FullName, StringComparer.Ordinal)
            .ToList();
    }

    private static HashSet<string> CollectTestTargetNames(
        AnalysisResult result,
        ImpactNode root,
        IReadOnlyList<ChangeImpactItem> impacted,
        IReadOnlyList<CqrsRequest> impactedRequests)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        void AddNode(string kind, string fullName)
        {
            if (kind is "request" or "handler" or "notificationHandler" or "validator" or "service" or "dbcontext" or "notification")
                names.Add(fullName);
        }

        AddNode(root.Kind, root.FullName);
        foreach (var item in impacted)
            AddNode(item.Kind, item.FullName);

        foreach (var req in impactedRequests)
        {
            names.Add(req.FullName);
            foreach (var h in req.Handlers)
                names.Add(h);
            foreach (var v in req.Validators)
                names.Add(v);
        }

        if (root.Kind == "project")
        {
            foreach (var req in result.Requests.Where(r => string.Equals(r.ProjectName, root.Name, StringComparison.OrdinalIgnoreCase)))
                names.Add(req.FullName);
            foreach (var h in result.Handlers.Where(h => string.Equals(h.ProjectName, root.Name, StringComparison.OrdinalIgnoreCase)))
                names.Add(h.FullName);
            foreach (var v in result.Validators.Where(v => string.Equals(v.ProjectName, root.Name, StringComparison.OrdinalIgnoreCase)))
                names.Add(v.FullName);
        }

        return names;
    }

    private static bool MatchesAnyTarget(string testTargetFullName, HashSet<string> targetNames)
    {
        if (targetNames.Contains(testTargetFullName))
            return true;

        if (!testTargetFullName.Contains('.', StringComparison.Ordinal))
        {
            foreach (var name in targetNames)
            {
                if (string.Equals(CSharpSyntax.SimpleName(name), testTargetFullName, StringComparison.Ordinal))
                    return true;
            }
        }

        return false;
    }

    private static string[] CollectUncoveredHotspots(
        IReadOnlyList<CqrsRequest> impactedRequests,
        IReadOnlyList<TestMethodReference> allTestRefs)
    {
        var hotspots = new List<string>();
        foreach (var req in impactedRequests)
        {
            var sliceTargets = new HashSet<string>(StringComparer.Ordinal) { req.FullName };
            foreach (var h in req.Handlers)
                sliceTargets.Add(h);
            foreach (var v in req.Validators)
                sliceTargets.Add(v);

            var hasTestRef = allTestRefs.Any(tr => MatchesAnyTarget(tr.TargetFullName, sliceTargets));
            if (!hasTestRef)
                hotspots.Add($"Untested request: {req.FullName} (0 static test references)");

            if (string.Equals(req.Kind, "Command", StringComparison.Ordinal) && req.Validators.Count == 0)
                hotspots.Add($"Unvalidated command: {req.FullName} (Command has no linked validator)");
        }

        return hotspots
            .Distinct(StringComparer.Ordinal)
            .OrderBy(h => h, StringComparer.Ordinal)
            .ToArray();
    }

    private static string[] CollectAffectedProjects(
        ImpactNode root,
        IReadOnlyList<ChangeImpactItem> impacted)
    {
        var projects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (root.Kind == "project")
            projects.Add(root.Name);
        else if (!string.IsNullOrWhiteSpace(root.ProjectName))
            projects.Add(root.ProjectName);

        foreach (var item in impacted)
        {
            if (item.Kind == "project")
                projects.Add(item.Name);
            else if (!string.IsNullOrWhiteSpace(item.ProjectName))
                projects.Add(item.ProjectName);
        }

        return projects
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p, StringComparer.Ordinal)
            .ToArray();
    }

    private static ChangeImpactBlindSpot[] CollectBlindSpots(AnalysisResult result)
    {
        var spots = new List<ChangeImpactBlindSpot>();
        var seen = new HashSet<(string Code, string Message)>(BlindSpotKeyComparer.Instance);

        void AddSpot(ChangeImpactBlindSpot spot)
        {
            if (seen.Add((spot.Code, spot.Message)))
                spots.Add(spot);
        }

        if (result.HasSourceFilters)
        {
            AddSpot(new ChangeImpactBlindSpot(
                "SourceFiltersActive",
                "Scan ran with include/exclude source filters; callers or dependencies in excluded files may be missing."));
        }

        if (!result.CanRunStrictChecks)
        {
            var codes = string.Join(", ", result.Diagnostics
                .Select(d => d.Code)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(c => c, StringComparer.Ordinal));
            AddSpot(new ChangeImpactBlindSpot(
                "IncompleteScan",
                $"Scan recorded error/incomplete diagnostics ({codes}); dependency edges may be incomplete."));
        }

        var testProjectNames = CollectTestProjectNames(result);

        foreach (var unresolved in result.UnresolvedDispatches
            .OrderBy(u => u.Location?.File, StringComparer.Ordinal)
            .ThenBy(u => u.Location?.Line)
            .ThenBy(u => u.Method, StringComparer.Ordinal)
            .ThenBy(u => u.ReceiverName, StringComparer.Ordinal))
        {
            if (unresolved.IsTestProject ||
                (!string.IsNullOrWhiteSpace(unresolved.ProjectName) && testProjectNames.Contains(unresolved.ProjectName)))
            {
                continue;
            }

            var receiver = string.IsNullOrWhiteSpace(unresolved.ReceiverName) ? "unknown receiver" : unresolved.ReceiverName;
            AddSpot(new ChangeImpactBlindSpot(
                "UnresolvedDispatch",
                $"Unresolved {unresolved.Method} dispatch on `{receiver}` cannot be traced statically.",
                unresolved.ProjectName,
                unresolved.Location));
        }

        foreach (var project in result.Projects.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            var location = SourceLocation.FromLine(project.FilePath, result.RootPath, 1, 1);
            foreach (var conditional in project.ConditionalReferences)
            {
                AddSpot(new ChangeImpactBlindSpot(
                    "ConditionalProjectReference",
                    $"Project {project.Name} has a conditional reference to {conditional.ResolvedProjectName ?? conditional.Include} (Condition: {conditional.Condition}).",
                    project.Name,
                    location));
            }
        }

        return spots.ToArray();
    }

    private static HashSet<string> CollectTestProjectNames(AnalysisResult result)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in result.Projects)
        {
            if (string.Equals(project.Layer, "Tests", StringComparison.OrdinalIgnoreCase))
                names.Add(project.Name);
        }

        foreach (var testProject in TestProjectDiscovery.DiscoverTestProjects(result))
            names.Add(testProject.Name);

        return names;
    }

    private sealed class BlindSpotKeyComparer : IEqualityComparer<(string Code, string Message)>
    {
        internal static readonly BlindSpotKeyComparer Instance = new();

        public bool Equals((string Code, string Message) x, (string Code, string Message) y)
            => string.Equals(x.Code, y.Code, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(x.Message, y.Message, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Code, string Message) obj)
            => HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Code),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Message));
    }

    private sealed record ImpactNode(
        string Id,
        string Kind,
        string Name,
        string FullName,
        string? ProjectName,
        SourceLocation? Location)
    {
        internal string DisplayPathLabel => $"{Kind}:{FullName}";

        internal ChangeImpactTarget ToTarget() => new(Name, Kind, FullName, ProjectName, Location);
    }

    private sealed record ImpactEdge(ImpactNode Source, ImpactNode Target, string Reason);

    private sealed class ImpactGraph
    {
        private readonly Dictionary<string, ImpactNode> nodes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<ImpactEdge>> outgoing = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<ImpactEdge>> incoming = new(StringComparer.Ordinal);
        private readonly HashSet<(string FromId, string ToId, string Reason)> seenEdges = new();

        private readonly Dictionary<string, ImpactNode> projectsByKey = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<ImpactNode>> projectsByName = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ImpactNode> requestsByFull = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<ImpactNode>> requestsBySimple = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ImpactNode> handlersByFull = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<ImpactNode>> handlersBySimple = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ImpactNode> validatorsByFull = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<ImpactNode>> validatorsBySimple = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ImpactNode> notificationsByFull = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<ImpactNode>> notificationsBySimple = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ImpactNode> dbContextsByFull = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<ImpactNode>> dbContextsBySimple = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ImpactNode> entitiesByFull = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<ImpactNode>> entitiesBySimple = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ImpactNode> servicesByFull = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<ImpactNode>> servicesBySimple = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ImpactNode> eventsByFull = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<ImpactNode>> eventsBySimple = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<ImpactNode>> entrypointAliases = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<SystemEntrypointInfo, ImpactNode> entrypointNodes = new();
        private readonly Dictionary<string, List<ImpactNode>> ownedByProject = new(StringComparer.OrdinalIgnoreCase);

        internal static ImpactGraph Build(AnalysisResult result)
        {
            var graph = new ImpactGraph();
            graph.Populate(result);
            return graph;
        }

        internal IReadOnlyList<ImpactNode> FindTargets(string query, string? targetKind)
        {
            var candidates = nodes.Values.Where(node =>
            {
                if (targetKind is null)
                {
                    return node.Kind is "project" or "entity" or "dbcontext" or "service" or
                        "notification" or "handler" or "request" or "entrypoint" or "event";
                }

                if (targetKind == "handler")
                    return node.Kind is "handler" or "notificationHandler";

                return node.Kind == targetKind;
            }).ToArray();

            var exactFull = candidates
                .Where(n => string.Equals(n.FullName, query, StringComparison.Ordinal))
                .ToArray();
            if (exactFull.Length > 0)
                return DeduplicateAutoNotificationEvent(exactFull, targetKind);

            var ciFull = candidates
                .Where(n => string.Equals(n.FullName, query, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (ciFull.Length > 0)
                return DeduplicateAutoNotificationEvent(ciFull, targetKind);

            if (!query.Contains('.', StringComparison.Ordinal))
            {
                var exactName = candidates
                    .Where(n => string.Equals(n.Name, query, StringComparison.Ordinal))
                    .ToArray();
                if (exactName.Length > 0)
                    return DeduplicateAutoNotificationEvent(exactName, targetKind);

                var ciName = candidates
                    .Where(n => string.Equals(n.Name, query, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (ciName.Length > 0)
                    return DeduplicateAutoNotificationEvent(ciName, targetKind);
            }

            if (targetKind is null or "entrypoint" &&
                entrypointAliases.TryGetValue(query, out var aliasMatches) &&
                aliasMatches.Count > 0)
            {
                var exactAlias = aliasMatches
                    .Where(n => string.Equals(n.Name, query, StringComparison.Ordinal) ||
                                string.Equals(n.FullName, query, StringComparison.Ordinal))
                    .ToArray();
                return exactAlias.Length > 0 ? exactAlias : aliasMatches.ToArray();
            }

            return [];
        }

        private static ImpactNode[] DeduplicateAutoNotificationEvent(ImpactNode[] matches, string? targetKind)
        {
            if (targetKind is not null || matches.Length <= 1)
                return matches;

            var hasNotification = matches.Any(m => m.Kind == "notification");
            var hasEvent = matches.Any(m => m.Kind == "event");
            if (hasNotification && hasEvent && matches.All(m => m.Kind is "notification" or "event"))
                return matches.Where(m => m.Kind == "notification").ToArray();

            return matches;
        }

        internal IReadOnlyList<ImpactEdge> GetOutgoing(ImpactNode node)
            => outgoing.TryGetValue(node.Id, out var list) ? list : [];

        internal IReadOnlyList<ImpactEdge> GetIncoming(ImpactNode node)
            => incoming.TryGetValue(node.Id, out var list) ? list : [];

        internal IReadOnlyList<ImpactNode> GetProjectOwnedNodes(ImpactNode projectNode)
            => ownedByProject.TryGetValue(projectNode.Id, out var list) ? list : [];

        private void Populate(AnalysisResult result)
        {
            RegisterProjects(result);
            RegisterCoreShapes(result);
            RegisterServices(result);
            RegisterSystemEntrypointsAndEvents(result);
            LinkProjectReferences(result);
            LinkCqrsAndFlow(result);
            LinkServiceDependenciesAndCalls(result);
            LinkServiceClassEffects(result);
            LinkSystemEntrypointsAndIntegrationFlows(result);
        }

        private void RegisterProjects(AnalysisResult result)
        {
            var duplicateNames = result.Projects
                .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var project in result.Projects)
            {
                var location = SourceLocation.FromLine(project.FilePath, result.RootPath, 1, 1);
                var identity = duplicateNames.Contains(project.Name)
                    ? location?.File ?? project.Name
                    : project.Name;
                var node = AddNode("project", project.Name, identity, project.Name, location);
                projectsByKey[identity] = node;
                if (!string.IsNullOrEmpty(project.FilePath))
                    projectsByKey[Path.GetFullPath(project.FilePath)] = node;
                AddToIndex(projectsByName, project.Name, node);
            }
        }

        private void RegisterCoreShapes(AnalysisResult result)
        {
            foreach (var request in result.Requests)
            {
                var node = AddNode("request", request.Name, request.FullName, request.ProjectName, request.Location);
                requestsByFull.TryAdd(request.FullName, node);
                AddToIndex(requestsBySimple, request.Name, node);
                TrackOwned(result, node);
            }

            foreach (var handler in result.Handlers)
            {
                var node = AddNode("handler", handler.Name, handler.FullName, handler.ProjectName, handler.Location);
                handlersByFull.TryAdd(handler.FullName, node);
                AddToIndex(handlersBySimple, handler.Name, node);
                TrackOwned(result, node);
            }

            foreach (var nh in result.NotificationHandlers)
            {
                var node = AddNode("notificationHandler", nh.Name, nh.FullName, nh.ProjectName, nh.Location);
                handlersByFull.TryAdd(nh.FullName, node);
                AddToIndex(handlersBySimple, nh.Name, node);
                TrackOwned(result, node);
            }

            foreach (var validator in result.Validators)
            {
                var node = AddNode("validator", validator.Name, validator.FullName, validator.ProjectName, validator.Location);
                validatorsByFull.TryAdd(validator.FullName, node);
                AddToIndex(validatorsBySimple, validator.Name, node);
                TrackOwned(result, node);
            }

            foreach (var notification in result.Notifications)
            {
                var node = AddNode("notification", notification.Name, notification.FullName, notification.ProjectName, notification.Location);
                notificationsByFull.TryAdd(notification.FullName, node);
                AddToIndex(notificationsBySimple, notification.Name, node);
                TrackOwned(result, node);
            }

            foreach (var dbContext in result.DbContexts)
            {
                var node = AddNode("dbcontext", dbContext.Name, dbContext.FullName, dbContext.ProjectName, dbContext.Location);
                dbContextsByFull.TryAdd(dbContext.FullName, node);
                AddToIndex(dbContextsBySimple, dbContext.Name, node);
                TrackOwned(result, node);

                foreach (var entity in dbContext.Entities)
                {
                    var entityNode = GetOrAddEntity(entity.Name, entity.FullName, entity.ProjectName ?? dbContext.ProjectName, entity.Location, result);
                    AddEdge(node, entityNode, "exposes entity");
                }

                foreach (var set in dbContext.DbSets)
                {
                    var entitySimple = CSharpScanner.SimpleName(set.Entity);
                    var entityNode = ResolveOrAddEntity(set.Entity, entitySimple, dbContext.ProjectName, dbContext.Location, result);
                    AddEdge(node, entityNode, $"exposes DbSet {set.Property}");
                }
            }

            // Capture entities referenced only via TouchedEntities.
            foreach (var label in result.Requests.SelectMany(r => r.TouchedEntities)
                .Concat(result.Handlers.SelectMany(h => h.TouchedEntities))
                .Concat(result.NotificationHandlers.SelectMany(nh => nh.TouchedEntities))
                .Concat(result.SystemEntrypoints.SelectMany(e => e.TouchedEntities))
                .Concat(result.ServiceClassEffects.Values.SelectMany(s => s.TouchedEntities)))
            {
                var entityName = ExtractEntityName(label);
                if (!string.IsNullOrWhiteSpace(entityName))
                    ResolveOrAddEntity(entityName, CSharpScanner.SimpleName(entityName), null, null, result);
            }

            foreach (var endpoint in result.Endpoints)
            {
                var full = $"{endpoint.DisplayLabel} ({endpoint.Name})";
                var node = AddNode("endpoint", endpoint.DisplayLabel, full, endpoint.ProjectName, endpoint.Location);
                TrackOwned(result, node);
            }
        }

        private void RegisterServices(AnalysisResult result)
        {
            foreach (var sr in result.ServiceResolutions)
            {
                var ifaceNode = EnsureServiceNode(sr.InterfaceName, InferProjectName(result, sr.InterfaceLocation), sr.InterfaceLocation, result);
                if (!string.IsNullOrWhiteSpace(sr.Implementation))
                {
                    var implLoc = sr.Candidates
                        .FirstOrDefault(c => string.Equals(c.Type, sr.Implementation, StringComparison.Ordinal))
                        ?.Location;
                    var implNode = ResolveExistingTargetNode(sr.Implementation)
                        ?? EnsureServiceNode(sr.Implementation, InferProjectName(result, implLoc), implLoc, result);
                    AddEdge(ifaceNode, implNode, "DI resolved implementation");
                }
            }

            foreach (var edge in result.ServiceCallEdges)
            {
                var fromNode = ResolveExistingTargetNode(edge.From)
                    ?? EnsureServiceNode(edge.From, edge.ProjectName ?? InferProjectName(result, edge.Location), edge.Location, result);
                var viaNode = ResolveExistingTargetNode(edge.Via)
                    ?? EnsureServiceNode(edge.Via, null, null, result);
                var toNode = ResolveExistingTargetNode(edge.To)
                    ?? EnsureServiceNode(edge.To, null, null, result);

                AddEdge(fromNode, viaNode, $"calls {edge.Method}");
                if (!ReferenceEquals(viaNode, toNode))
                    AddEdge(viaNode, toNode, "DI resolved target");
            }

            foreach (var edge in result.ResolvedEdges)
            {
                var fromNode = ResolveExistingTargetNode(edge.From)
                    ?? EnsureServiceNode(edge.From, null, null, result);
                var viaNode = ResolveExistingTargetNode(edge.Via)
                    ?? EnsureServiceNode(edge.Via, null, null, result);
                var toNode = ResolveExistingTargetNode(edge.To)
                    ?? EnsureServiceNode(edge.To, edge.ProjectName, edge.Location, result);

                AddEdge(fromNode, viaNode, "injects interface");
                if (!ReferenceEquals(viaNode, toNode))
                    AddEdge(viaNode, toNode, "DI resolved DbContext");
            }
        }

        private void LinkProjectReferences(AnalysisResult result)
        {
            var byFullPath = result.Projects
                .Where(p => !string.IsNullOrEmpty(p.FilePath))
                .ToDictionary(p => Path.GetFullPath(p.FilePath), p => p, StringComparer.OrdinalIgnoreCase);

            foreach (var project in result.Projects)
            {
                if (!TryGetProjectNode(project, out var fromNode))
                    continue;

                if (project.ReferenceIncludes.Count > 0)
                {
                    foreach (var include in project.ReferenceIncludes)
                    {
                        var path = ProjectReferenceResolver.ResolvePath(project.DirectoryPath, include);
                        if (path is not null &&
                            byFullPath.TryGetValue(path, out var targetProject) &&
                            TryGetProjectNode(targetProject, out var toNode))
                        {
                            AddEdge(fromNode, toNode, "references project");
                        }
                    }

                    continue;
                }

                foreach (var refName in project.ReferenceNames)
                {
                    if (projectsByName.TryGetValue(refName, out var targets) && targets.Count == 1)
                        AddEdge(fromNode, targets[0], "references project");
                }
            }
        }

        private void LinkCqrsAndFlow(AnalysisResult result)
        {
            var duplicateRequestNames = result.Requests
                .GroupBy(r => r.Name, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var endpoint in result.Endpoints)
            {
                var endpointFull = $"{endpoint.DisplayLabel} ({endpoint.Name})";
                if (!nodes.TryGetValue(NodeId("endpoint", endpointFull), out var endpointNode))
                    continue;

                var targetReq = ResolveEndpointRequest(result, endpoint, duplicateRequestNames);
                if (targetReq is not null && requestsByFull.TryGetValue(targetReq.FullName, out var reqNode))
                    AddEdge(endpointNode, reqNode, "dispatches request");
            }

            foreach (var request in result.Requests)
            {
                var reqNode = requestsByFull[request.FullName];

                foreach (var handlerName in request.Handlers)
                {
                    if (ResolveNode(handlerName, handlersByFull, handlersBySimple) is { } handlerNode)
                        AddEdge(reqNode, handlerNode, "handled by");
                }

                foreach (var validatorName in request.Validators)
                {
                    if (ResolveNode(validatorName, validatorsByFull, validatorsBySimple) is { } validatorNode)
                        AddEdge(validatorNode, reqNode, "validates request");
                }

                foreach (var pubName in request.PublishedNotifications)
                {
                    if (ResolveNode(pubName, notificationsByFull, notificationsBySimple) is { } notifNode)
                        AddEdge(reqNode, notifNode, "publishes notification");
                }

                foreach (var dispName in request.DispatchedRequests)
                {
                    if (ResolveNode(dispName, requestsByFull, requestsBySimple) is { } dispNode)
                        AddEdge(reqNode, dispNode, "dispatches request");
                }

                foreach (var entityLabel in request.TouchedEntities)
                {
                    var entitySimple = ExtractEntityName(entityLabel);
                    if (ResolveNode(entitySimple, entitiesByFull, entitiesBySimple) is { } entityNode)
                        AddEdge(reqNode, entityNode, "touches entity");
                }

                foreach (var prop in request.Properties)
                {
                    LinkTypeTokensToEntities(reqNode, prop.Type, "references entity in property");
                }

                if (!string.IsNullOrWhiteSpace(request.ResponseType))
                    LinkTypeTokensToEntities(reqNode, request.ResponseType, "returns entity");
            }

            foreach (var handler in result.Handlers)
            {
                var handlerNode = handlersByFull[handler.FullName];
                foreach (var pubName in handler.PublishedNotifications)
                {
                    if (ResolveNode(pubName, notificationsByFull, notificationsBySimple) is { } notifNode)
                        AddEdge(handlerNode, notifNode, "publishes notification");
                }

                foreach (var dispName in handler.DispatchedRequests)
                {
                    if (ResolveNode(dispName, requestsByFull, requestsBySimple) is { } reqNode)
                        AddEdge(handlerNode, reqNode, "dispatches request");
                }

                foreach (var entityLabel in handler.TouchedEntities)
                {
                    var entitySimple = ExtractEntityName(entityLabel);
                    if (ResolveNode(entitySimple, entitiesByFull, entitiesBySimple) is { } entityNode)
                        AddEdge(handlerNode, entityNode, "touches entity");
                }
            }

            foreach (var notification in result.Notifications)
            {
                var notifNode = notificationsByFull[notification.FullName];
                foreach (var nhName in notification.Handlers)
                {
                    if (ResolveNode(nhName, handlersByFull, handlersBySimple) is { } nhNode)
                        AddEdge(notifNode, nhNode, "handled by notification handler");
                }
            }

            foreach (var nh in result.NotificationHandlers)
            {
                var nhNode = handlersByFull[nh.FullName];
                if (ResolveNode(nh.NotificationName, notificationsByFull, notificationsBySimple) is { } notifNode)
                    AddEdge(notifNode, nhNode, "handled by notification handler");

                foreach (var pubName in nh.PublishedNotifications)
                {
                    if (ResolveNode(pubName, notificationsByFull, notificationsBySimple) is { } pubNode)
                        AddEdge(nhNode, pubNode, "publishes notification");
                }

                foreach (var dispName in nh.DispatchedRequests)
                {
                    if (ResolveNode(dispName, requestsByFull, requestsBySimple) is { } reqNode)
                        AddEdge(nhNode, reqNode, "dispatches request");
                }

                foreach (var entityLabel in nh.TouchedEntities)
                {
                    var entitySimple = ExtractEntityName(entityLabel);
                    if (ResolveNode(entitySimple, entitiesByFull, entitiesBySimple) is { } entityNode)
                        AddEdge(nhNode, entityNode, "touches entity");
                }
            }

            foreach (var edge in result.FlowEdges)
            {
                switch (edge.Kind)
                {
                    case FlowEdgeKind.HandlerToDbContext:
                    {
                        if (ResolveNode(edge.From, handlersByFull, handlersBySimple) is { } handlerNode &&
                            ResolveNode(edge.To, dbContextsByFull, dbContextsBySimple) is { } dbNode)
                        {
                            AddEdge(handlerNode, dbNode, "uses DbContext");
                        }
                        break;
                    }
                    case FlowEdgeKind.HandlerToNotification:
                    {
                        if (ResolveNode(edge.From, handlersByFull, handlersBySimple) is { } handlerNode &&
                            ResolveNode(edge.To, notificationsByFull, notificationsBySimple) is { } notifNode)
                        {
                            AddEdge(handlerNode, notifNode, "publishes notification");
                        }
                        break;
                    }
                    case FlowEdgeKind.HandlerToRequest:
                    {
                        if (ResolveNode(edge.From, handlersByFull, handlersBySimple) is { } handlerNode &&
                            ResolveNode(edge.To, requestsByFull, requestsBySimple) is { } reqNode)
                        {
                            AddEdge(handlerNode, reqNode, "dispatches request");
                        }
                        break;
                    }
                }
            }

            foreach (var site in result.DispatchCallSites)
            {
                if (site.IsTestProject || string.IsNullOrWhiteSpace(site.SenderFullName))
                    continue;

                var senderNode = ResolveExistingTargetNode(site.SenderFullName);
                if (senderNode is null)
                    continue;

                if (site.TargetKind == DispatchTargetKind.Request &&
                    requestsByFull.TryGetValue(site.TargetFullName, out var reqNode))
                {
                    AddEdge(senderNode, reqNode, "dispatches request");
                }
                else if (site.TargetKind == DispatchTargetKind.Notification &&
                         notificationsByFull.TryGetValue(site.TargetFullName, out var notifNode))
                {
                    AddEdge(senderNode, notifNode, "publishes notification");
                }
            }
        }

        private void LinkServiceDependenciesAndCalls(AnalysisResult result)
        {
            void LinkDependencies(ImpactNode ownerNode, IReadOnlyList<string> dependencies)
            {
                foreach (var dep in dependencies)
                {
                    var (depBase, _) = CSharpSyntax.ParseTypeReference(dep);
                    if (depBase.Length == 0)
                        continue;

                    if (ResolveNode(depBase, dbContextsByFull, dbContextsBySimple) is { } dbNode)
                    {
                        AddEdge(ownerNode, dbNode, "injects DbContext");
                        continue;
                    }

                    if (ResolveNode(depBase, servicesByFull, servicesBySimple) is { } serviceNode)
                        AddEdge(ownerNode, serviceNode, "injects dependency");
                }
            }

            foreach (var handler in result.Handlers)
                LinkDependencies(handlersByFull[handler.FullName], handler.Dependencies);

            foreach (var nh in result.NotificationHandlers)
                LinkDependencies(handlersByFull[nh.FullName], nh.Dependencies);
        }

        private void RegisterSystemEntrypointsAndEvents(AnalysisResult result)
        {
            foreach (var flow in result.IntegrationFlows)
            {
                if (flow.Kind != IntegrationFlowKind.IntegrationEvent)
                    continue;

                var eventName = string.IsNullOrWhiteSpace(flow.MessageType) ? flow.Label : flow.MessageType!;
                if (string.IsNullOrWhiteSpace(eventName))
                    continue;

                ResolveOrAddEvent(eventName, CSharpScanner.SimpleName(eventName), flow.FromProject, flow.Location, result);
            }

            var seenEntrypoints = new HashSet<(string Project, SystemEntrypointKind Kind, string Method, string Route, string Name)>();
            var uniqueEntrypoints = new List<SystemEntrypointInfo>(result.SystemEntrypoints.Count);
            foreach (var ep in result.SystemEntrypoints)
            {
                var key = (ep.ProjectName ?? string.Empty, ep.Kind, ep.Method, ep.RouteOrTrigger, ep.Name);
                if (seenEntrypoints.Add(key))
                    uniqueEntrypoints.Add(ep);
            }

            var baseNameCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var ep in uniqueEntrypoints)
            {
                var baseName = EntrypointBaseName(ep);
                baseNameCounts[baseName] = baseNameCounts.TryGetValue(baseName, out var count) ? count + 1 : 1;
            }

            foreach (var ep in uniqueEntrypoints)
            {
                var baseName = EntrypointBaseName(ep);
                var fullName = baseNameCounts[baseName] > 1
                    ? MakeUniqueEntrypointFullName(ep, baseName)
                    : baseName;

                var node = AddNode("entrypoint", baseName, fullName, ep.ProjectName, ep.Location);
                entrypointNodes[ep] = node;
                TrackOwned(result, node);

                RegisterEntrypointAlias(baseName, node);
                RegisterEntrypointAlias(fullName, node);
                RegisterEntrypointAlias(ep.Name, node);
                RegisterEntrypointAlias(ep.DisplayLabel, node);
                if (ep.Kind != SystemEntrypointKind.MessageConsumer)
                    RegisterEntrypointAlias(ep.RouteOrTrigger, node);
                else if (!string.IsNullOrWhiteSpace(ep.RouteOrTrigger))
                    ResolveOrAddEvent(ep.RouteOrTrigger, CSharpScanner.SimpleName(ep.RouteOrTrigger), ep.ProjectName, ep.Location, result);

                RegisterPublishedMessagesAsEvents(ep.PublishedMessages, ep.ProjectName, ep.Location, result);
            }

            foreach (var effect in result.ServiceClassEffects.Values)
                RegisterPublishedMessagesAsEvents(effect.PublishedMessages, effect.ProjectName, effect.Location, result);
        }

        private void RegisterPublishedMessagesAsEvents(
            IEnumerable<string> messages,
            string? projectName,
            SourceLocation? location,
            AnalysisResult result)
        {
            foreach (var msg in messages)
            {
                if (string.IsNullOrWhiteSpace(msg))
                    continue;

                if (ResolveNode(msg, eventsByFull, eventsBySimple) is null &&
                    ResolveNode(msg, notificationsByFull, notificationsBySimple) is null)
                {
                    ResolveOrAddEvent(msg, CSharpScanner.SimpleName(msg), projectName, location, result);
                }
            }
        }

        private void LinkServiceClassEffects(AnalysisResult result)
        {
            if (result.ServiceClassEffects.Count == 0)
                return;

            var entrypointOwners = BuildEntrypointOwnerClassSet(result);

            foreach (var (serviceFullName, effect) in result.ServiceClassEffects
                .OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                if (handlersByFull.ContainsKey(serviceFullName) ||
                    dbContextsByFull.ContainsKey(serviceFullName) ||
                    requestsByFull.ContainsKey(serviceFullName) ||
                    notificationsByFull.ContainsKey(serviceFullName) ||
                    validatorsByFull.ContainsKey(serviceFullName))
                {
                    continue;
                }

                // Entrypoint owners (controllers, gRPC services, workers, consumers) are linked
                // via their entrypoint nodes unless also resolved/called as a service target.
                if (!servicesByFull.ContainsKey(serviceFullName))
                {
                    var proj = effect.ProjectName ?? string.Empty;
                    var simple = CSharpScanner.SimpleName(serviceFullName);
                    if (entrypointOwners.Contains((proj, serviceFullName)) ||
                        entrypointOwners.Contains((proj, simple)))
                    {
                        continue;
                    }
                }

                var svcNode = ResolveExistingTargetNode(serviceFullName)
                    ?? EnsureServiceNode(serviceFullName, effect.ProjectName, effect.Location, result);

                LinkCalledServicesForNode(svcNode, effect.ProjectName, effect.CalledServices, result);
                LinkPersistenceAndMessagesForNode(
                    svcNode,
                    effect.TouchedDbContexts,
                    effect.TouchedEntities,
                    effect.PublishedMessages);
            }
        }

        private static HashSet<(string Project, string ClassName)> BuildEntrypointOwnerClassSet(AnalysisResult result)
        {
            var owners = new HashSet<(string Project, string ClassName)>();
            foreach (var ep in result.SystemEntrypoints)
            {
                if (string.IsNullOrWhiteSpace(ep.Name) || ep.Name.Contains(' ', StringComparison.Ordinal))
                    continue;

                var proj = ep.ProjectName ?? string.Empty;
                owners.Add((proj, ep.Name));
                var dot = ep.Name.LastIndexOf('.');
                if (dot > 0 && dot < ep.Name.Length - 1)
                    owners.Add((proj, ep.Name[..dot]));
            }

            return owners;
        }

        private void LinkCalledServicesForNode(
            ImpactNode ownerNode,
            string? fallbackProjectName,
            IReadOnlyList<string> calledServices,
            AnalysisResult result)
        {
            foreach (var rawSvc in calledServices)
            {
                var arrow = rawSvc.IndexOf(" -> ", StringComparison.Ordinal);
                if (arrow >= 0)
                {
                    var ifaceName = rawSvc[..arrow].Trim();
                    var implName = rawSvc[(arrow + 4)..].Trim();
                    var ifaceNode = ResolveExistingTargetNode(ifaceName)
                        ?? EnsureServiceNode(ifaceName, null, null, result);
                    result.ServiceClassEffects.TryGetValue(implName, out var implEffect);
                    var implNode = ResolveExistingTargetNode(implName)
                        ?? EnsureServiceNode(
                            implName,
                            implEffect?.ProjectName ?? fallbackProjectName,
                            implEffect?.Location,
                            result);
                    AddEdge(ownerNode, ifaceNode, "calls service");
                    if (!ReferenceEquals(ifaceNode, implNode))
                        AddEdge(ifaceNode, implNode, "DI resolved implementation");
                }
                else if (!string.IsNullOrWhiteSpace(rawSvc))
                {
                    var trimmed = rawSvc.Trim();
                    result.ServiceClassEffects.TryGetValue(trimmed, out var svcEffect);
                    var svcNode = ResolveExistingTargetNode(trimmed)
                        ?? EnsureServiceNode(
                            trimmed,
                            svcEffect?.ProjectName ?? fallbackProjectName,
                            svcEffect?.Location,
                            result);
                    AddEdge(ownerNode, svcNode, "calls service");
                }
            }
        }

        private void LinkPersistenceAndMessagesForNode(
            ImpactNode ownerNode,
            IReadOnlyList<string> dbContexts,
            IReadOnlyList<string> entities,
            IReadOnlyList<string> publishedMessages)
        {
            foreach (var dbName in dbContexts)
            {
                if (ResolveNode(dbName, dbContextsByFull, dbContextsBySimple) is { } dbNode)
                    AddEdge(ownerNode, dbNode, "uses DbContext");
            }

            foreach (var entityLabel in entities)
            {
                var entitySimple = ExtractEntityName(entityLabel);
                if (!string.IsNullOrWhiteSpace(entitySimple) &&
                    ResolveNode(entitySimple, entitiesByFull, entitiesBySimple) is { } entityNode)
                {
                    AddEdge(ownerNode, entityNode, "touches entity");
                }
            }

            foreach (var msg in publishedMessages)
            {
                if (string.IsNullOrWhiteSpace(msg))
                    continue;

                if (ResolveNode(msg, eventsByFull, eventsBySimple) is { } eventNode)
                {
                    AddEdge(ownerNode, eventNode, "publishes event");
                }
                else if (ResolveNode(msg, notificationsByFull, notificationsBySimple) is { } notifNode)
                {
                    AddEdge(ownerNode, notifNode, "publishes notification");
                }
            }
        }

        private void LinkSystemEntrypointsAndIntegrationFlows(AnalysisResult result)
        {
            foreach (var (ep, epNode) in entrypointNodes)
            {
                foreach (var reqName in ep.TargetRequests)
                {
                    if (ResolveNode(reqName, requestsByFull, requestsBySimple) is { } reqNode)
                        AddEdge(epNode, reqNode, "dispatches request");
                }

                if (ep.Kind == SystemEntrypointKind.MessageConsumer &&
                    !string.IsNullOrWhiteSpace(ep.RouteOrTrigger) &&
                    ResolveNode(ep.RouteOrTrigger, eventsByFull, eventsBySimple) is { } consumedEventNode)
                {
                    AddEdge(consumedEventNode, epNode, "consumed by");
                }

                // CQRS entrypoints already flow through Request -> Handler -> DbContext/Entity/Notification.
                // Direct/Worker/Consumer entrypoints (or CQRS entrypoints without resolved requests) link directly.
                if (ep.ExecutionStyle == SystemExecutionStyle.Cqrs && ep.TargetRequests.Count > 0)
                    continue;

                LinkCalledServicesForNode(epNode, ep.ProjectName, ep.CalledServices, result);
                LinkPersistenceAndMessagesForNode(
                    epNode,
                    ep.TouchedDbContexts,
                    ep.TouchedEntities,
                    ep.PublishedMessages);
            }

            foreach (var flow in result.IntegrationFlows)
            {
                var fromProjNode = ResolveUniqueProject(flow.FromProject);
                var toProjNode = ResolveUniqueProject(flow.ToProject);

                if (flow.Kind == IntegrationFlowKind.IntegrationEvent)
                {
                    var eventName = string.IsNullOrWhiteSpace(flow.MessageType) ? flow.Label : flow.MessageType!;
                    var eventNode = !string.IsNullOrWhiteSpace(eventName)
                        ? ResolveNode(eventName, eventsByFull, eventsBySimple)
                        : null;

                    if (eventNode is not null)
                    {
                        if (fromProjNode is not null)
                            AddEdge(fromProjNode, eventNode, "publishes integration event");
                        if (toProjNode is not null)
                            AddEdge(eventNode, toProjNode, "consumes integration event");
                    }
                    else if (fromProjNode is not null && toProjNode is not null)
                    {
                        AddEdge(fromProjNode, toProjNode, $"publishes integration event ({flow.Label})");
                    }

                    continue;
                }

                if (fromProjNode is null || toProjNode is null)
                    continue;

                var reason = flow.Kind switch
                {
                    IntegrationFlowKind.AspireReference => $"Aspire reference ({flow.Label})",
                    IntegrationFlowKind.HttpClient => $"calls via HttpClient ({flow.Label})",
                    IntegrationFlowKind.GrpcClient => $"calls via gRPC ({flow.Label})",
                    IntegrationFlowKind.SharedDbContext => $"shares DbContext ({flow.MessageType ?? flow.Label})",
                    _ => flow.Label,
                };
                AddEdge(fromProjNode, toProjNode, reason);

                if (flow.Kind == IntegrationFlowKind.SharedDbContext &&
                    !string.IsNullOrWhiteSpace(flow.MessageType) &&
                    ResolveNode(flow.MessageType, dbContextsByFull, dbContextsBySimple) is { } sharedDbNode)
                {
                    AddEdge(fromProjNode, sharedDbNode, "uses DbContext");
                    AddEdge(toProjNode, sharedDbNode, "uses DbContext");
                }
            }
        }

        private static string EntrypointBaseName(SystemEntrypointInfo ep)
        {
            var isGenericMapName = ep.Name is "MapGet" or "MapPost" or "MapPut" or "MapDelete" or "MapPatch";
            return !isGenericMapName && !string.IsNullOrWhiteSpace(ep.Name)
                ? ep.Name
                : ep.DisplayLabel;
        }

        private string MakeUniqueEntrypointFullName(SystemEntrypointInfo ep, string baseName)
        {
            var candidate = !string.IsNullOrWhiteSpace(ep.RouteOrTrigger) &&
                            !string.Equals(ep.RouteOrTrigger, baseName, StringComparison.Ordinal)
                ? $"{baseName} :: {ep.DisplayLabel}"
                : $"{baseName} :: {ep.ProjectName ?? "unknown"}";
            if (!nodes.ContainsKey(NodeId("entrypoint", candidate)))
                return candidate;

            return $"{candidate} [{ep.ProjectName ?? "unknown"}]";
        }

        private void RegisterEntrypointAlias(string? alias, ImpactNode node)
        {
            if (string.IsNullOrWhiteSpace(alias))
                return;
            AddToIndex(entrypointAliases, alias.Trim(), node);
        }

        private ImpactNode? ResolveUniqueProject(string? projectName)
        {
            if (string.IsNullOrWhiteSpace(projectName))
                return null;
            return projectsByName.TryGetValue(projectName, out var list) && list.Count == 1
                ? list[0]
                : null;
        }

        private ImpactNode ResolveOrAddEvent(
            string fullOrSimpleName,
            string simpleName,
            string? projectName,
            SourceLocation? location,
            AnalysisResult result)
        {
            if (ResolveNode(fullOrSimpleName, eventsByFull, eventsBySimple) is { } existing)
                return existing;

            var node = AddNode("event", simpleName, fullOrSimpleName, projectName, location);
            eventsByFull.TryAdd(fullOrSimpleName, node);
            AddToIndex(eventsBySimple, simpleName, node);
            TrackOwned(result, node);
            return node;
        }

        private void LinkTypeTokensToEntities(ImpactNode owner, string typeText, string reason)
        {
            var start = -1;
            for (var i = 0; i <= typeText.Length; i++)
            {
                var isIdent = i < typeText.Length && (char.IsLetterOrDigit(typeText[i]) || typeText[i] == '_');
                if (isIdent)
                {
                    if (start < 0)
                        start = i;
                    continue;
                }

                if (start >= 0)
                {
                    var token = typeText.Substring(start, i - start);
                    start = -1;
                    if (ResolveNode(token, entitiesByFull, entitiesBySimple) is { } entityNode)
                        AddEdge(owner, entityNode, reason);
                }
            }
        }

        private ImpactNode EnsureServiceNode(
            string fullOrSimpleName,
            string? projectName,
            SourceLocation? location,
            AnalysisResult result)
        {
            if (ResolveNode(fullOrSimpleName, servicesByFull, servicesBySimple) is { } existing)
                return existing;

            var simple = CSharpScanner.SimpleName(fullOrSimpleName);
            var node = AddNode("service", simple, fullOrSimpleName, projectName, location);
            servicesByFull.TryAdd(fullOrSimpleName, node);
            AddToIndex(servicesBySimple, simple, node);
            TrackOwned(result, node);
            return node;
        }

        private static string? InferProjectName(AnalysisResult result, SourceLocation? location)
        {
            if (location is null || string.IsNullOrEmpty(result.RootPath))
                return null;

            var fullPath = Path.Combine(result.RootPath, location.File.Replace('/', Path.DirectorySeparatorChar));
            return ProjectLocator.FindOwningProject(result.Projects, fullPath)?.Name;
        }

        private ImpactNode? ResolveExistingTargetNode(string fullName)
        {
            if (handlersByFull.TryGetValue(fullName, out var h))
                return h;
            if (dbContextsByFull.TryGetValue(fullName, out var db))
                return db;
            if (requestsByFull.TryGetValue(fullName, out var r))
                return r;
            if (notificationsByFull.TryGetValue(fullName, out var n))
                return n;
            if (servicesByFull.TryGetValue(fullName, out var s))
                return s;
            return null;
        }

        private ImpactNode GetOrAddEntity(
            string name,
            string fullName,
            string? projectName,
            SourceLocation? location,
            AnalysisResult result)
        {
            if (entitiesByFull.TryGetValue(fullName, out var existing))
                return existing;

            var node = AddNode("entity", name, fullName, projectName, location);
            entitiesByFull.TryAdd(fullName, node);
            AddToIndex(entitiesBySimple, name, node);
            TrackOwned(result, node);
            return node;
        }

        private ImpactNode ResolveOrAddEntity(
            string fullOrSimpleName,
            string simpleName,
            string? projectName,
            SourceLocation? location,
            AnalysisResult result)
        {
            if (ResolveNode(fullOrSimpleName, entitiesByFull, entitiesBySimple) is { } existing)
                return existing;

            return GetOrAddEntity(simpleName, fullOrSimpleName, projectName, location, result);
        }

        private static CqrsRequest? ResolveEndpointRequest(
            AnalysisResult result,
            EndpointInfo endpoint,
            HashSet<string> duplicateRequestNames)
        {
            if (!string.IsNullOrEmpty(endpoint.TargetRequestFullName))
            {
                return result.Requests.FirstOrDefault(r =>
                    string.Equals(r.FullName, endpoint.TargetRequestFullName, StringComparison.Ordinal));
            }

            if (!string.IsNullOrEmpty(endpoint.TargetRequestNamespace))
            {
                return result.Requests.FirstOrDefault(r =>
                    string.Equals(r.Namespace, endpoint.TargetRequestNamespace, StringComparison.Ordinal) &&
                    string.Equals(r.Name, endpoint.TargetRequestName, StringComparison.Ordinal));
            }

            if (!duplicateRequestNames.Contains(endpoint.TargetRequestName))
            {
                return result.Requests.FirstOrDefault(r =>
                    string.Equals(r.Name, endpoint.TargetRequestName, StringComparison.Ordinal));
            }

            return null;
        }

        private void TrackOwned(AnalysisResult result, ImpactNode node)
        {
            ProjectInfo? owner = null;
            if (node.Location is not null && !string.IsNullOrEmpty(result.RootPath))
            {
                var fullPath = Path.Combine(result.RootPath, node.Location.File.Replace('/', Path.DirectorySeparatorChar));
                owner = ProjectLocator.FindOwningProject(result.Projects, fullPath);
            }

            if (owner is not null && TryGetProjectNode(owner, out var projNode))
            {
                AddToIndex(ownedByProject, projNode.Id, node);
                return;
            }

            if (!string.IsNullOrWhiteSpace(node.ProjectName) &&
                projectsByName.TryGetValue(node.ProjectName, out var list) &&
                list.Count == 1)
            {
                AddToIndex(ownedByProject, list[0].Id, node);
            }
        }

        private bool TryGetProjectNode(ProjectInfo project, out ImpactNode node)
        {
            if (!string.IsNullOrEmpty(project.FilePath) &&
                projectsByKey.TryGetValue(Path.GetFullPath(project.FilePath), out node!))
            {
                return true;
            }

            if (projectsByKey.TryGetValue(project.Name, out node!))
                return true;

            node = null!;
            return false;
        }

        private ImpactNode AddNode(
            string kind,
            string name,
            string fullName,
            string? projectName,
            SourceLocation? location)
        {
            var id = NodeId(kind, fullName);
            if (nodes.TryGetValue(id, out var existing))
                return existing;

            var node = new ImpactNode(id, kind, name, fullName, projectName, location);
            nodes[id] = node;
            outgoing[id] = new List<ImpactEdge>();
            incoming[id] = new List<ImpactEdge>();
            return node;
        }

        private void AddEdge(ImpactNode source, ImpactNode target, string reason)
        {
            if (ReferenceEquals(source, target))
                return;

            if (!seenEdges.Add((source.Id, target.Id, reason)))
                return;

            var edge = new ImpactEdge(source, target, reason);
            outgoing[source.Id].Add(edge);
            incoming[target.Id].Add(edge);
        }

        private static ImpactNode? ResolveNode(
            string name,
            Dictionary<string, ImpactNode> byFull,
            Dictionary<string, List<ImpactNode>> bySimple)
        {
            if (byFull.TryGetValue(name, out var exact))
                return exact;

            var simple = CSharpScanner.SimpleName(name);
            if (bySimple.TryGetValue(simple, out var list) && list.Count == 1)
                return list[0];

            return null;
        }

        private static void AddToIndex(Dictionary<string, List<ImpactNode>> index, string key, ImpactNode node)
        {
            if (!index.TryGetValue(key, out var list))
                index[key] = list = new List<ImpactNode>();
            if (!list.Contains(node))
                list.Add(node);
        }

        private static string NodeId(string kind, string fullName) => $"{kind}\u0000{fullName}";

        private static string ExtractEntityName(string label)
        {
            var open = label.IndexOf('(');
            var close = label.LastIndexOf(')');
            return open >= 0 && close > open + 1
                ? label[(open + 1)..close].Trim()
                : label.Trim();
        }
    }
}
