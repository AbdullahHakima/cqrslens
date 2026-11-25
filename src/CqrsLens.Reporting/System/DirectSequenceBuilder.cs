namespace CqrsLens;

/// <summary>
/// Builds clean Mermaid <c>sequenceDiagram</c>s for direct non-CQRS operations
/// (Minimal APIs, MVC controllers, gRPC services, background workers, and message
/// consumers) without any synthetic <c>Mediator</c> participant.
/// </summary>
internal static class DirectSequenceBuilder
{
    internal const int DefaultMaxSequences = 12;
    private const int FocusedMaxSequences = 40;
    private const int MaxServicesPerSequence = 5;
    private const int MaxDbContextsPerSequence = 3;
    private const int MaxEventsPerSequence = 4;
    private const int MaxParticipantLabelLength = 40;

    internal static IReadOnlyList<DirectSequence> Build(
        AnalysisResult result,
        string? focusService = null,
        bool excludeTests = false,
        int maxSequences = DefaultMaxSequences)
    {
        ArgumentNullException.ThrowIfNull(result);

        var eligible = SystemViewFilter.VisibleEntrypoints(result, focusService, excludeTests)
            .Where(DirectFlowDiagramBuilder.IsEligibleDirectEntrypoint)
            .OrderByDescending(DirectFlowDiagramBuilder.RichnessScore)
            .ThenBy(e => e.ProjectName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Kind)
            .ThenBy(e => e.DisplayLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Name, StringComparer.Ordinal)
            .ToList();

        if (eligible.Count == 0)
            return Array.Empty<DirectSequence>();

        var hasMatchedFocus = !string.IsNullOrWhiteSpace(focusService) &&
                              SystemViewFilter.ResolveFocus(result, focusService).Count > 0;
        var cap = hasMatchedFocus
            ? FocusedMaxSequences
            : (maxSequences > 0 ? maxSequences : DefaultMaxSequences);

        var contextsByFull = result.DbContexts
            .GroupBy(c => c.FullName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var resolvedDbByService = result.ResolvedEdges
            .Where(e => e.Kind == FlowEdgeKind.HandlerToDbContextResolved)
            .GroupBy(e => e.From, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => e.To).ToHashSet(StringComparer.Ordinal),
                StringComparer.Ordinal);

        var sequences = new List<DirectSequence>();
        foreach (var ep in eligible.Take(cap))
        {
            var seq = BuildOne(ep, contextsByFull, resolvedDbByService);
            if (MermaidDiagramText.IsWithinMermaidLimits(seq.Source))
                sequences.Add(seq);
        }

        return sequences;
    }

    private static DirectSequence BuildOne(
        SystemEntrypointInfo ep,
        IReadOnlyDictionary<string, DbContextInfo> contextsByFull,
        IReadOnlyDictionary<string, HashSet<string>> resolvedDbByService)
    {
        var opTitle = OperationTitle(ep);
        var lines = new List<string>
        {
            "sequenceDiagram",
            $"    %% Operation sequence: {Sanitize(opTitle)}",
        };

        var participants = new List<(string Id, string Label)>();
        var epLabel = DirectFlowDiagramBuilder.TruncateLabel(ParticipantLabel(ep), MaxParticipantLabelLength);
        participants.Add(("ep", epLabel));

        var services = ep.CalledServices.Take(MaxServicesPerSequence).ToList();
        var serviceIds = new List<(string Id, string FullName)>(services.Count);
        for (var i = 0; i < services.Count; i++)
        {
            var id = $"s{i + 1}";
            serviceIds.Add((id, services[i]));
            participants.Add((id, DirectFlowDiagramBuilder.TruncateLabel(
                MermaidDiagramText.ShortTypeName(services[i]), MaxParticipantLabelLength)));
        }

        var contexts = ep.TouchedDbContexts.Take(MaxDbContextsPerSequence).ToList();
        var contextIds = new List<(string Id, string FullName)>(contexts.Count);
        for (var i = 0; i < contexts.Count; i++)
        {
            var id = $"c{i + 1}";
            contextIds.Add((id, contexts[i]));
            var ctx = contextsByFull.TryGetValue(contexts[i], out var c) ? c : null;
            participants.Add((id, DirectFlowDiagramBuilder.TruncateLabel(
                ctx?.Name ?? MermaidDiagramText.ShortTypeName(contexts[i]), MaxParticipantLabelLength)));
        }

        var events = ep.PublishedMessages.Take(MaxEventsPerSequence).ToList();
        if (events.Count > 0)
            participants.Add(("bus", "Event Bus"));

        foreach (var (id, label) in participants)
            lines.Add($"    participant {id} as {Sanitize(label)}");

        void Arrow(string from, string to, string text, bool dashed = false)
            => lines.Add($"    {from}{(dashed ? "-->>" : "->>")}{to}: {Sanitize(text)}");

        if (serviceIds.Count > 0)
        {
            foreach (var (svcId, _) in serviceIds)
                Arrow("ep", svcId, "calls");

            var primaryDbSvc = serviceIds.FirstOrDefault(s => !LooksLikeBusOrMessaging(s.FullName));
            if (primaryDbSvc.Id is null)
                primaryDbSvc = serviceIds[0];

            var primaryPubSvc = serviceIds.FirstOrDefault(s => LooksLikeBusOrMessaging(s.FullName));
            if (primaryPubSvc.Id is null)
                primaryPubSvc = serviceIds[0];

            foreach (var (ctxId, ctxFull) in contextIds)
            {
                var dbOwner = serviceIds.FirstOrDefault(s =>
                    resolvedDbByService.TryGetValue(s.FullName, out var set) && set.Contains(ctxFull));
                var fromId = dbOwner.Id ?? primaryDbSvc.Id;
                var usesText = FormatUsesText(ep, ctxFull, contextsByFull);
                Arrow(fromId, ctxId, usesText, dashed: true);
            }

            foreach (var evt in events)
                Arrow(primaryPubSvc.Id, "bus", $"publishes {MermaidDiagramText.ShortTypeName(evt)}", dashed: true);

            if (ep.Kind is SystemEntrypointKind.Http or SystemEntrypointKind.Grpc && serviceIds.Count == 1)
                Arrow(serviceIds[0].Id, "ep", "response", dashed: true);
        }
        else
        {
            foreach (var (ctxId, ctxFull) in contextIds)
                Arrow("ep", ctxId, FormatUsesText(ep, ctxFull, contextsByFull));

            foreach (var evt in events)
                Arrow("ep", "bus", $"publishes {MermaidDiagramText.ShortTypeName(evt)}", dashed: true);
        }

        var source = string.Join("\n", lines);
        var kindLabel = SystemViewFilter.KindLabel(ep.Kind);
        var projPrefix = string.IsNullOrWhiteSpace(ep.ProjectName) ? "" : $"[{ep.ProjectName}] ";
        var caption = $"{projPrefix}{kindLabel} sequence for {opTitle}: " +
                      BuildCaptionSummary(serviceIds.Count, contextIds.Count, events.Count);

        return new DirectSequence(
            opTitle,
            ep.DisplayLabel,
            ep.ProjectName,
            kindLabel,
            source,
            caption);
    }

    private static string OperationTitle(SystemEntrypointInfo ep)
        => ep.Kind switch
        {
            SystemEntrypointKind.Grpc => DirectFlowDiagramBuilder.KeepClassAndMember(
                string.IsNullOrWhiteSpace(ep.RouteOrTrigger) ? ep.Name : ep.RouteOrTrigger),
            SystemEntrypointKind.Worker => DirectFlowDiagramBuilder.KeepClassAndMember(
                string.IsNullOrWhiteSpace(ep.RouteOrTrigger) ? ep.Name : ep.RouteOrTrigger),
            SystemEntrypointKind.MessageConsumer => string.IsNullOrWhiteSpace(ep.RouteOrTrigger)
                ? ep.Name
                : $"{ep.Name} ({MermaidDiagramText.ShortTypeName(ep.RouteOrTrigger)})",
            _ => !string.IsNullOrWhiteSpace(ep.RouteOrTrigger)
                ? $"{ep.Method} {ep.RouteOrTrigger} ({ep.Name})"
                : ep.DisplayLabel,
        };

    private static string ParticipantLabel(SystemEntrypointInfo ep)
        => ep.Kind switch
        {
            SystemEntrypointKind.Grpc => "gRPC " + DirectFlowDiagramBuilder.KeepClassAndMember(
                string.IsNullOrWhiteSpace(ep.RouteOrTrigger) ? ep.Name : ep.RouteOrTrigger),
            SystemEntrypointKind.Worker => "Worker " + DirectFlowDiagramBuilder.KeepClassAndMember(
                string.IsNullOrWhiteSpace(ep.RouteOrTrigger) ? ep.Name : ep.RouteOrTrigger),
            SystemEntrypointKind.MessageConsumer => "Consumer " + MermaidDiagramText.ShortTypeName(
                string.IsNullOrWhiteSpace(ep.RouteOrTrigger) ? ep.Name : ep.RouteOrTrigger),
            _ => ep.DisplayLabel,
        };

    private static string FormatUsesText(
        SystemEntrypointInfo ep,
        string ctxFull,
        IReadOnlyDictionary<string, DbContextInfo> contextsByFull)
    {
        if (ep.TouchedEntities.Count == 0)
            return "uses";

        if (contextsByFull.TryGetValue(ctxFull, out var ctx) && ctx.DbSets.Count > 0)
        {
            var touchedSet = new HashSet<string>(ep.TouchedEntities, StringComparer.Ordinal);
            var matched = ctx.DbSets
                .Where(d =>
                    touchedSet.Contains($"{d.Property} ({SimpleLeaf(d.Entity)})") ||
                    touchedSet.Contains(d.Entity) ||
                    touchedSet.Contains(SimpleLeaf(d.Entity)))
                .Select(d => SimpleLeaf(d.Entity))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();
            if (matched.Count > 0)
            {
                var shown = matched.Take(3).ToList();
                var suffix = matched.Count > 3 ? $" +{matched.Count - 3}" : "";
                return $"uses {string.Join(", ", shown)}{suffix}";
            }
        }

        var entities = ep.TouchedEntities
            .Select(AgentReportFormatter.EntityNameFromLabel)
            .Select(SimpleLeaf)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        if (entities.Count == 0)
            return "uses";

        var top = entities.Take(3).ToList();
        var extra = entities.Count > 3 ? $" +{entities.Count - 3}" : "";
        return $"uses {string.Join(", ", top)}{extra}";
    }

    private static string BuildCaptionSummary(int services, int dbContexts, int events)
    {
        var parts = new List<string>();
        if (services > 0)
            parts.Add(MermaidDiagramText.CountText(services, "service"));
        if (dbContexts > 0)
            parts.Add(MermaidDiagramText.CountText(dbContexts, "DbContext"));
        if (events > 0)
            parts.Add(MermaidDiagramText.CountText(events, "published event"));
        return string.Join(" · ", parts) + ".";
    }

    private static bool LooksLikeBusOrMessaging(string serviceName)
        => serviceName.Contains("EventBus", StringComparison.OrdinalIgnoreCase) ||
           serviceName.Contains("IntegrationEvent", StringComparison.OrdinalIgnoreCase) ||
           serviceName.Contains("Publisher", StringComparison.OrdinalIgnoreCase) ||
           serviceName.Contains("MessageBus", StringComparison.OrdinalIgnoreCase);

    private static string SimpleLeaf(string value)
    {
        var dot = value.LastIndexOf('.');
        return dot < 0 ? value : value[(dot + 1)..];
    }

    private static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        var noSemicolons = value.Replace(';', ',');
        return MermaidDiagramText.MermaidLabel(noSemicolons).Trim();
    }
}
