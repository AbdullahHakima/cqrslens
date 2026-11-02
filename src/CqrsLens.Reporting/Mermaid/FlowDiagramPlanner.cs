namespace CqrsLens;

/// <summary>
/// Partitions the Phase 4 flow edges into bounded, feature-labelled diagrams.
/// Every flow edge is assigned to exactly one section; when a feature is too large
/// it is split by owner (request/handler) so no edge is dropped.
/// </summary>
internal static class FlowDiagramPlanner
{
    private sealed record Entry(string FeatureKey, string FeatureLabel, string OwnerKey, FlowEdge Edge);

    internal static IReadOnlyList<FlowDiagramSection> Sections(AnalysisResult result)
    {
        var distinctEdges = result.FlowEdges
            .GroupBy(e => (e.Kind, e.From, e.To))
            .Select(g => g.First())
            .OrderBy(e => e.Kind)
            .ThenBy(e => e.From, StringComparer.Ordinal)
            .ThenBy(e => e.To, StringComparer.Ordinal)
            .ToList();
        if (distinctEdges.Count == 0)
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
        {
            var ownerRequest = OwnerRequest(edge, handlerOwner);
            if (ownerRequest is not null && requestByFull.TryGetValue(ownerRequest, out var request))
            {
                entries.Add(new Entry(
                    CqrsRequestOrdering.FeatureKey(request),
                    CqrsRequestOrdering.FeatureHeading(request),
                    request.FullName,
                    edge));
                continue;
            }

            if (notificationHandlerFeature.TryGetValue(edge.From, out var handlerFeature))
            {
                entries.Add(new Entry(
                    CqrsRequestOrdering.FeatureKey(handlerFeature.Project, handlerFeature.Namespace),
                    CqrsRequestOrdering.FeatureHeading(handlerFeature.Project, handlerFeature.Namespace),
                    edge.From,
                    edge));
                continue;
            }

            handlerProject.TryGetValue(edge.From, out var project);
            var label = string.IsNullOrEmpty(project) ? MermaidDiagramText.UnassignedProject : project!;
            entries.Add(new Entry($"project :: {label}", label, edge.From, edge));
        }

        var sections = new List<FlowDiagramSection>();
        foreach (var feature in entries
                     .GroupBy(e => e.FeatureKey, StringComparer.Ordinal)
                     .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var label = feature.First().FeatureLabel;
            var blocks = feature
                .GroupBy(e => e.OwnerKey, StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => g.OrderBy(e => e.Edge.Kind).ThenBy(e => e.Edge.From, StringComparer.Ordinal)
                    .ThenBy(e => e.Edge.To, StringComparer.Ordinal).ToList())
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
                    OwnerCount: parts[i].OwnerCount));
            }
        }

        return sections;
    }

    private static string? OwnerRequest(FlowEdge edge, Dictionary<string, string> handlerOwner) => edge.Kind switch
    {
        FlowEdgeKind.EndpointToRequest => edge.To,
        FlowEdgeKind.RequestToHandler => edge.From,
        FlowEdgeKind.HandlerToRequest =>
            handlerOwner.TryGetValue(edge.From, out var owner) ? owner : edge.To,
        FlowEdgeKind.HandlerToNotification or FlowEdgeKind.HandlerToDbContext =>
            handlerOwner.TryGetValue(edge.From, out var owner) ? owner : null,
        _ => null,
    };

    private static List<(string Source, IReadOnlyList<FlowEdge> Edges, int OwnerCount)> SplitToBounded(
        string label,
        List<List<Entry>> blocks)
    {
        var results = new List<(string, IReadOnlyList<FlowEdge>, int)>();
        var pending = new Stack<List<List<Entry>>>();
        pending.Push(blocks);

        while (pending.Count > 0)
        {
            var chunk = pending.Pop();
            var edges = chunk
                .SelectMany(b => b.Select(e => e.Edge))
                .OrderBy(e => e.Kind)
                .ThenBy(e => e.From, StringComparer.Ordinal)
                .ThenBy(e => e.To, StringComparer.Ordinal)
                .ToList();
            var source = FlowDiagramBuilder.BuildSource(edges, label);
            if (chunk.Count <= 1 || DiagramBuilder.IsWithinMermaidLimits(source))
            {
                results.Add((source, edges, chunk.Count));
                continue;
            }

            var middle = (chunk.Count + 1) / 2;
            pending.Push(chunk.Skip(middle).ToList());
            pending.Push(chunk.Take(middle).ToList());
        }

        return results;
    }
}
