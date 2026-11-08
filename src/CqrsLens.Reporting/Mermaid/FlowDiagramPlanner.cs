namespace CqrsLens;

/// <summary>
/// Partitions the Phase 4 flow edges into bounded, feature-labelled diagrams.
/// Every flow edge is assigned to exactly one section; when a feature is too large
/// it is split by owner (request/handler) so no edge is dropped. DI-resolved edges
/// (handler → DbContext through an injected interface) travel with their handler's
/// feature and are drawn as a separate, dotted hop.
/// </summary>
internal static class FlowDiagramPlanner
{
    private sealed record Entry(
        string FeatureKey,
        string FeatureLabel,
        string OwnerKey,
        FlowEdge? Flow,
        ResolvedEdge? Resolved);

    internal static IReadOnlyList<FlowDiagramSection> Sections(AnalysisResult result)
    {
        var distinctEdges = result.FlowEdges
            .GroupBy(e => (e.Kind, e.From, e.To))
            .Select(g => g.First())
            .OrderBy(e => e.Kind)
            .ThenBy(e => e.From, StringComparer.Ordinal)
            .ThenBy(e => e.To, StringComparer.Ordinal)
            .ToList();

        var distinctResolved = result.ResolvedEdges
            .GroupBy(e => (e.Kind, e.From, e.Via, e.To))
            .Select(g => g.First())
            .OrderBy(e => e.From, StringComparer.Ordinal)
            .ThenBy(e => e.Via, StringComparer.Ordinal)
            .ThenBy(e => e.To, StringComparer.Ordinal)
            .ToList();

        if (distinctEdges.Count == 0 && distinctResolved.Count == 0)
            return Array.Empty<FlowDiagramSection>();

        var requestByFull = result.Requests
            .GroupBy(r => r.FullName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        // The first request a handler serves, in canonical request order. A handler
        // that serves several requests is attributed to one of them so its outgoing
        // edges are never counted twice.
        var handlerOwner = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var request in result.Requests.OrderBy(r => r.FullName, StringComparer.Ordinal))
        {
            foreach (var handler in request.Handlers)
                handlerOwner.TryAdd(handler, request.FullName);
        }

        var handlerProject = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var handler in result.Handlers)
            handlerProject.TryAdd(handler.FullName, handler.ProjectName);
        foreach (var handler in result.NotificationHandlers)
            handlerProject.TryAdd(handler.FullName, handler.ProjectName);

        // Notification handlers that publish integration events belong to a feature
        // too; index their namespace so their publish edges join that feature's flow
        // section instead of a trailing per-project bucket.
        var notificationHandlerFeature = new Dictionary<string, (string? Project, string? Namespace)>(StringComparer.Ordinal);
        foreach (var handler in result.NotificationHandlers)
            notificationHandlerFeature.TryAdd(handler.FullName, (handler.ProjectName, handler.Namespace));

        var entries = new List<Entry>();
        foreach (var edge in distinctEdges)
            entries.Add(FlowEntry(edge, requestByFull, handlerOwner, handlerProject, notificationHandlerFeature));
        foreach (var edge in distinctResolved)
            entries.Add(ResolvedEntry(edge, requestByFull, handlerOwner, handlerProject, notificationHandlerFeature));

        var sections = new List<FlowDiagramSection>();
        foreach (var feature in entries
                     .GroupBy(e => e.FeatureKey, StringComparer.Ordinal)
                     .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var label = feature.First().FeatureLabel;
            var blocks = feature
                .GroupBy(e => e.OwnerKey, StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => g
                    .OrderBy(SortKind)
                    .ThenBy(SortFrom, StringComparer.Ordinal)
                    .ThenBy(SortTo, StringComparer.Ordinal)
                    .ToList())
                .ToList();

            var parts = SplitToBounded(label, blocks);
            for (var i = 0; i < parts.Count; i++)
            {
                sections.Add(new FlowDiagramSection(
                    label,
                    parts[i].Source,
                    parts[i].Edges,
                    Part: i + 1,
                    PartCount: parts.Count,
                    OwnerCount: parts[i].OwnerCount)
                {
                    ResolvedEdges = parts[i].ResolvedEdges,
                });
            }
        }

        return sections;
    }

    private static Entry FlowEntry(
        FlowEdge edge,
        IReadOnlyDictionary<string, CqrsRequest> requestByFull,
        IReadOnlyDictionary<string, string> handlerOwner,
        IReadOnlyDictionary<string, string?> handlerProject,
        IReadOnlyDictionary<string, (string? Project, string? Namespace)> notificationHandlerFeature)
    {
        var ownerRequest = OwnerRequest(edge, handlerOwner);
        if (ownerRequest is not null && requestByFull.TryGetValue(ownerRequest, out var request))
            return Feature(request.FullName, request, edge);

        if (notificationHandlerFeature.TryGetValue(edge.From, out var handlerFeature))
            return NotificationFeature(edge.From, handlerFeature.Project, handlerFeature.Namespace, edge);

        handlerProject.TryGetValue(edge.From, out var project);
        return ProjectBucket(edge.From, project, edge);
    }

    private static Entry ResolvedEntry(
        ResolvedEdge edge,
        IReadOnlyDictionary<string, CqrsRequest> requestByFull,
        IReadOnlyDictionary<string, string> handlerOwner,
        IReadOnlyDictionary<string, string?> handlerProject,
        IReadOnlyDictionary<string, (string? Project, string? Namespace)> notificationHandlerFeature)
    {
        if (handlerOwner.TryGetValue(edge.From, out var ownerRequest) &&
            requestByFull.TryGetValue(ownerRequest, out var request))
            return Feature(request.FullName, request, edge);

        if (notificationHandlerFeature.TryGetValue(edge.From, out var handlerFeature))
            return NotificationFeature(edge.From, handlerFeature.Project, handlerFeature.Namespace, edge);

        handlerProject.TryGetValue(edge.From, out var project);
        return ProjectBucket(edge.From, project, edge);
    }

    private static Entry Feature(string ownerKey, CqrsRequest request, FlowEdge edge)
        => new(CqrsRequestOrdering.FeatureKey(request), CqrsRequestOrdering.FeatureHeading(request), ownerKey, edge, null);

    private static Entry Feature(string ownerKey, CqrsRequest request, ResolvedEdge edge)
        => new(CqrsRequestOrdering.FeatureKey(request), CqrsRequestOrdering.FeatureHeading(request), ownerKey, null, edge);

    private static Entry NotificationFeature(string ownerKey, string? project, string? ns, FlowEdge edge)
        => new(CqrsRequestOrdering.FeatureKey(project, ns), CqrsRequestOrdering.FeatureHeading(project, ns), ownerKey, edge, null);

    private static Entry NotificationFeature(string ownerKey, string? project, string? ns, ResolvedEdge edge)
        => new(CqrsRequestOrdering.FeatureKey(project, ns), CqrsRequestOrdering.FeatureHeading(project, ns), ownerKey, null, edge);

    private static Entry ProjectBucket(string ownerKey, string? project, FlowEdge edge)
    {
        var label = string.IsNullOrEmpty(project) ? MermaidDiagramText.UnassignedProject : project!;
        return new Entry($"project :: {label}", label, ownerKey, edge, null);
    }

    private static Entry ProjectBucket(string ownerKey, string? project, ResolvedEdge edge)
    {
        var label = string.IsNullOrEmpty(project) ? MermaidDiagramText.UnassignedProject : project!;
        return new Entry($"project :: {label}", label, ownerKey, null, edge);
    }

    private static FlowEdgeKind SortKind(Entry entry) => entry.Flow?.Kind ?? entry.Resolved!.Kind;

    private static string SortFrom(Entry entry) => entry.Flow?.From ?? entry.Resolved!.From;

    private static string SortTo(Entry entry) => entry.Flow?.To ?? entry.Resolved!.To;

    private static string? OwnerRequest(FlowEdge edge, IReadOnlyDictionary<string, string> handlerOwner) => edge.Kind switch
    {
        FlowEdgeKind.EndpointToRequest => edge.To,
        FlowEdgeKind.RequestToHandler => edge.From,
        FlowEdgeKind.HandlerToRequest =>
            handlerOwner.TryGetValue(edge.From, out var owner) ? owner : edge.To,
        FlowEdgeKind.HandlerToNotification or FlowEdgeKind.HandlerToDbContext =>
            handlerOwner.TryGetValue(edge.From, out var owner) ? owner : null,
        _ => null,
    };

    private static List<(string Source, IReadOnlyList<FlowEdge> Edges, IReadOnlyList<ResolvedEdge> ResolvedEdges, int OwnerCount)> SplitToBounded(
        string label,
        List<List<Entry>> blocks)
    {
        var results = new List<(string, IReadOnlyList<FlowEdge>, IReadOnlyList<ResolvedEdge>, int)>();
        var pending = new Stack<List<List<Entry>>>();
        pending.Push(blocks);

        while (pending.Count > 0)
        {
            var chunk = pending.Pop();
            var edges = chunk
                .SelectMany(b => b)
                .Where(e => e.Flow is not null)
                .Select(e => e.Flow!)
                .OrderBy(e => e.Kind)
                .ThenBy(e => e.From, StringComparer.Ordinal)
                .ThenBy(e => e.To, StringComparer.Ordinal)
                .ToList();
            var resolvedEdges = chunk
                .SelectMany(b => b)
                .Where(e => e.Resolved is not null)
                .Select(e => e.Resolved!)
                .OrderBy(e => e.Kind)
                .ThenBy(e => e.From, StringComparer.Ordinal)
                .ThenBy(e => e.Via, StringComparer.Ordinal)
                .ThenBy(e => e.To, StringComparer.Ordinal)
                .ToList();
            var source = FlowDiagramBuilder.BuildSource(edges, resolvedEdges, label);
            if (chunk.Count <= 1 || DiagramBuilder.IsWithinMermaidLimits(source))
            {
                results.Add((source, edges, resolvedEdges, chunk.Count));
                continue;
            }

            var middle = (chunk.Count + 1) / 2;
            pending.Push(chunk.Skip(middle).ToList());
            pending.Push(chunk.Take(middle).ToList());
        }

        return results;
    }
}
