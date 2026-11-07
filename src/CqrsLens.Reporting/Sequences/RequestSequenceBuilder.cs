using System.Text;

namespace CqrsLens;

/// <summary>
/// Builds the per-request sequence diagrams for the reports. Rendering only: it reads
/// the existing model (requests, endpoints, flow edges, notifications, the Stage 2
/// registered behavior order) and invents nothing. A request with no resolved handler
/// gets no sequence; a request with no handler→DbContext edge shows no data
/// participant. DbContext/notification hops appear only when the model holds the edge.
/// </summary>
internal static class RequestSequenceBuilder
{
    internal const string RegisteredOrderNote =
        "Registered order — execution order is not guaranteed.";

    internal static IReadOnlyList<RequestSequence> Build(AnalysisResult result)
    {
        if (result.Requests.Count == 0)
            return Array.Empty<RequestSequence>();

        var endpointsByRequest = result.Endpoints
            .Where(e => !string.IsNullOrEmpty(e.TargetRequestFullName))
            .GroupBy(e => e.TargetRequestFullName!, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g
                    .OrderBy(e => e.HttpMethod, StringComparer.Ordinal)
                    .ThenBy(e => e.Route, StringComparer.Ordinal)
                    .ThenBy(e => e.Name, StringComparer.Ordinal)
                    .First(),
                StringComparer.Ordinal);

        var contextsByFull = result.DbContexts
            .GroupBy(c => c.FullName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var notificationsByFull = result.Notifications
            .GroupBy(n => n.FullName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var touchedByHandler = result.Handlers
            .GroupBy(h => h.FullName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.First().TouchedEntities, StringComparer.Ordinal);

        var handlerDbContexts = result.FlowEdges
            .Where(e => e.Kind == FlowEdgeKind.HandlerToDbContext)
            .GroupBy(e => e.From, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => e.To).Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToList(),
                StringComparer.Ordinal);

        var handlerNotifications = result.FlowEdges
            .Where(e => e.Kind == FlowEdgeKind.HandlerToNotification)
            .GroupBy(e => e.From, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => e.To).Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToList(),
                StringComparer.Ordinal);

        var behaviors = BehaviorOrdering.Flattened(result.Behaviors);

        var sequences = new List<RequestSequence>();
        foreach (var request in result.Requests
                     .Where(r => r.Handlers.Count > 0)
                     .OrderBy(r => r.Name, StringComparer.Ordinal)
                     .ThenBy(r => r.FullName, StringComparer.Ordinal))
        {
            sequences.Add(BuildOne(
                request,
                endpointsByRequest,
                behaviors,
                contextsByFull,
                touchedByHandler,
                handlerDbContexts,
                handlerNotifications,
                notificationsByFull));
        }

        return sequences;
    }

    private static RequestSequence BuildOne(
        CqrsRequest request,
        IReadOnlyDictionary<string, EndpointInfo> endpointsByRequest,
        IReadOnlyList<BehaviorInfo> behaviors,
        IReadOnlyDictionary<string, DbContextInfo> contextsByFull,
        IReadOnlyDictionary<string, IReadOnlyList<string>> touchedByHandler,
        IReadOnlyDictionary<string, List<string>> handlerDbContexts,
        IReadOnlyDictionary<string, List<string>> handlerNotifications,
        IReadOnlyDictionary<string, CqrsNotification> notificationsByFull)
    {
        var lines = new List<string>
        {
            "sequenceDiagram",
            $"    %% Request sequence: {Sanitize(request.Name)}",
        };

        var participants = new List<(string Id, string Label)>();
        var hasEndpoint = endpointsByRequest.TryGetValue(request.FullName, out var endpoint);

        const string mediatorId = "m";
        var behaviorIds = new List<string>();
        var handlerIds = new List<(string Id, string FullName)>();
        var contextIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var notificationIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var notificationHandlerIds = new List<(string Id, string FullName, string NotificationFullName)>();
        var notificationHandlerSeen = new HashSet<string>(StringComparer.Ordinal);

        // 1. Endpoint (only when an endpoint edge exists).
        if (hasEndpoint)
            participants.Add(("ep", endpoint!.DisplayLabel));

        // 2. Mediator.
        participants.Add((mediatorId, "Mediator"));

        // 3. Pipeline behaviors, in Stage 2 registered order.
        for (var i = 0; i < behaviors.Count; i++)
        {
            var id = $"b{i + 1}";
            behaviorIds.Add(id);
            participants.Add((id, behaviors[i].Name));
        }

        // 4. Handler(s).
        var handlerIndex = 0;
        foreach (var handler in request.Handlers.OrderBy(h => h, StringComparer.Ordinal))
        {
            handlerIndex++;
            var id = "h" + handlerIndex;
            handlerIds.Add((id, handler));
            participants.Add((id, MermaidDiagramText.ShortTypeName(handler)));
        }

        // 5. DbContext(s) and 6. notifications → their handlers, per handler.
        foreach (var (_, handlerFull) in handlerIds)
        {
            if (handlerDbContexts.TryGetValue(handlerFull, out var contextFulls))
            {
                foreach (var contextFull in contextFulls)
                {
                    if (contextIds.ContainsKey(contextFull))
                        continue;
                    var context = contextsByFull.TryGetValue(contextFull, out var c) ? c : null;
                    var id = "c" + (contextIds.Count + 1);
                    contextIds[contextFull] = id;
                    participants.Add((id, context?.Name ?? MermaidDiagramText.ShortTypeName(contextFull)));
                }
            }

            if (handlerNotifications.TryGetValue(handlerFull, out var notificationFulls))
            {
                foreach (var notificationFull in notificationFulls)
                {
                    if (notificationIds.ContainsKey(notificationFull))
                        continue;

                    var notification = notificationsByFull.TryGetValue(notificationFull, out var n) ? n : null;
                    var id = "n" + (notificationIds.Count + 1);
                    notificationIds[notificationFull] = id;
                    participants.Add((id, notification?.Name ?? MermaidDiagramText.ShortTypeName(notificationFull)));

                    if (notification is null)
                        continue;

                    foreach (var nh in notification.Handlers.OrderBy(h => h, StringComparer.Ordinal))
                    {
                        if (!notificationHandlerSeen.Add(nh))
                            continue;
                        var nhId = $"nh{notificationHandlerIds.Count + 1}";
                        notificationHandlerIds.Add((nhId, nh, notificationFull));
                        participants.Add((nhId, MermaidDiagramText.ShortTypeName(nh)));
                    }
                }
            }
        }

        // Declare participants in order, then messages.
        foreach (var (id, label) in participants)
            lines.Add($"    participant {id} as {Sanitize(label)}");

        void Arrow(string from, string to, string text, bool dashed = false)
            => lines.Add($"    {from}{(dashed ? "-->>" : "->>")}{to}: {Sanitize(text)}");

        // Entry and behavior chain.
        if (hasEndpoint)
            Arrow("ep", mediatorId, request.Name);

        if (behaviorIds.Count > 0)
        {
            Arrow(mediatorId, behaviorIds[0], "handle");
            for (var i = 0; i + 1 < behaviorIds.Count; i++)
                Arrow(behaviorIds[i], behaviorIds[i + 1], "next");
            foreach (var (handlerId, _) in handlerIds)
                Arrow(behaviorIds[^1], handlerId, "handle");
        }
        else
        {
            foreach (var (handlerId, _) in handlerIds)
                Arrow(mediatorId, handlerId, "handle");
        }

        // DbContext and notification hops.
        foreach (var (handlerId, handlerFull) in handlerIds)
        {
            if (handlerDbContexts.TryGetValue(handlerFull, out var contextFulls))
            {
                foreach (var contextFull in contextFulls)
                {
                    var dsets = TouchedDbSets(contextFull, handlerFull, contextsByFull, touchedByHandler);
                    Arrow(handlerId, contextIds[contextFull], dsets.Count == 0 ? "uses" : $"uses {string.Join(", ", dsets)}");
                }
            }

            if (handlerNotifications.TryGetValue(handlerFull, out var notificationFulls))
            {
                foreach (var notificationFull in notificationFulls)
                {
                    Arrow(handlerId, notificationIds[notificationFull], "publish");
                    foreach (var (nhId, _, nhNotification) in notificationHandlerIds)
                    {
                        if (string.Equals(nhNotification, notificationFull, StringComparison.Ordinal))
                            Arrow(notificationIds[notificationFull], nhId, "handle");
                    }
                }
            }
        }

        // Return arrows: handler → mediator → caller (caller only when an endpoint edge exists).
        foreach (var (handlerId, _) in handlerIds)
            Arrow(handlerId, mediatorId, "response", dashed: true);
        if (hasEndpoint)
            Arrow(mediatorId, "ep", "response", dashed: true);

        // Standing label as a Mermaid note whenever behaviors are shown.
        if (behaviorIds.Count > 0)
        {
            var span = behaviorIds.Count == 1
                ? behaviorIds[0]
                : $"{behaviorIds[0]},{behaviorIds[^1]}";
            lines.Add($"    Note over {span}: {Sanitize(RegisteredOrderNote)}");
        }

        var source = string.Join("\n", lines);
        var caption = hasEndpoint
            ? $"Sequence for {request.Name}: {endpoint!.DisplayLabel} → mediator → handler."
            : $"Sequence for {request.Name}: mediator → handler (no endpoint edge in the model).";
        if (behaviorIds.Count > 0)
            caption += $" Behaviors shown in registered order ({behaviorIds.Count}); execution order is not guaranteed.";

        return new RequestSequence(request.Name, request.FullName, source, caption);
    }

    private static IReadOnlyList<string> TouchedDbSets(
        string contextFull,
        string handlerFull,
        IReadOnlyDictionary<string, DbContextInfo> contextsByFull,
        IReadOnlyDictionary<string, IReadOnlyList<string>> touchedByHandler)
    {
        if (!contextsByFull.TryGetValue(contextFull, out var context) ||
            !touchedByHandler.TryGetValue(handlerFull, out var touched) ||
            touched.Count == 0)
            return Array.Empty<string>();

        var touchedSet = new HashSet<string>(touched, StringComparer.Ordinal);
        return context.DbSets
            // The model labels DbSet work as "Property (Entity)" (DbSetMappingBuilder);
            // an entity-only fallback maps the entity name to itself. Match either, and
            // tolerate a qualified entity name by matching its simple leaf as well.
            .Where(d =>
                touchedSet.Contains($"{d.Property} ({SimpleLeaf(d.Entity)})") ||
                touchedSet.Contains(d.Entity) ||
                touchedSet.Contains(SimpleLeaf(d.Entity)))
            .Select(d => d.Property)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
    }

    private static string SimpleLeaf(string value)
    {
        var dot = value.LastIndexOf('.');
        return dot < 0 ? value : value[(dot + 1)..];
    }

    /// <summary>
    /// One-line, Mermaid-safe participant/message/note text. Mermaid entities are
    /// used for the characters that would otherwise terminate a label; statement
    /// separators are removed.
    /// </summary>
    private static string Sanitize(string? value)
    {
        var escaped = MermaidDiagramText.MermaidLabel(value);
        var sb = new StringBuilder(escaped.Length);
        foreach (var c in escaped)
            sb.Append(c == ';' ? ',' : c);
        return sb.ToString().Trim();
    }
}
