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
        var resolvedStoreByService = result.ServiceClassEffects
            .Where(kv => kv.Value.TouchedDataStores.Count > 0)
            .ToDictionary(
                kv => kv.Key,
                kv => kv.Value.TouchedDataStores.ToHashSet(StringComparer.Ordinal),
                StringComparer.Ordinal);

        var selected = hasMatchedFocus || eligible.Count <= cap
            ? eligible.Take(cap).ToList()
            : SelectDiverse(eligible, cap);

        var sequences = new List<DirectSequence>();
        foreach (var ep in selected)
        {
            var seq = BuildOne(ep, contextsByFull, resolvedDbByService, resolvedStoreByService);
            if (MermaidDiagramText.IsWithinMermaidLimits(seq.Source))
                sequences.Add(seq);
        }

        return sequences;
    }

    private static List<SystemEntrypointInfo> SelectDiverse(
        IReadOnlyList<SystemEntrypointInfo> ranked,
        int cap)
    {
        const int maxPerOwner = 2;
        var selected = new List<SystemEntrypointInfo>(cap);
        var selectedIndices = new HashSet<int>();
        var perOwner = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < ranked.Count && selected.Count < cap; i++)
        {
            var ep = ranked[i];
            var dot = ep.Name.LastIndexOf('.');
            var owner = dot > 0 ? ep.Name[..dot] : ep.Name;
            var ownerKey = $"{ep.ProjectName}|{owner}";
            var count = perOwner.GetValueOrDefault(ownerKey);
            if (count >= maxPerOwner)
                continue;

            perOwner[ownerKey] = count + 1;
            selected.Add(ep);
            selectedIndices.Add(i);
        }

        for (var i = 0; i < ranked.Count && selected.Count < cap; i++)
        {
            if (selectedIndices.Add(i))
                selected.Add(ranked[i]);
        }

        return selected;
    }

    private static DirectSequence BuildOne(
        SystemEntrypointInfo ep,
        IReadOnlyDictionary<string, DbContextInfo> contextsByFull,
        IReadOnlyDictionary<string, HashSet<string>> resolvedDbByService,
        IReadOnlyDictionary<string, HashSet<string>> resolvedStoreByService)
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
        var shortContextNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < contexts.Count; i++)
        {
            var id = $"c{i + 1}";
            contextIds.Add((id, contexts[i]));
            var ctx = contextsByFull.TryGetValue(contexts[i], out var c) ? c : null;
            var shortName = ctx?.Name ?? MermaidDiagramText.ShortTypeName(contexts[i]);
            shortContextNames.Add(shortName);
            participants.Add((id, DirectFlowDiagramBuilder.TruncateLabel(shortName, MaxParticipantLabelLength)));
        }

        var dataStores = ep.TouchedDataStores
            .Where(ds => !shortContextNames.Contains(ds))
            .Take(MaxDbContextsPerSequence)
            .ToList();
        var dataStoreIds = new List<(string Id, string Name)>(dataStores.Count);
        for (var i = 0; i < dataStores.Count; i++)
        {
            var id = $"d{i + 1}";
            dataStoreIds.Add((id, dataStores[i]));
            participants.Add((id, DirectFlowDiagramBuilder.TruncateLabel(dataStores[i], MaxParticipantLabelLength)));
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

            foreach (var (dsId, dsName) in dataStoreIds)
            {
                var dsOwner = serviceIds.FirstOrDefault(s =>
                    resolvedStoreByService.TryGetValue(ServiceImplName(s.FullName), out var set) && set.Contains(dsName));
                var fromId = dsOwner.Id ?? primaryDbSvc.Id;
                var usesText = FormatDataStoreUsesText(ep);
                Arrow(fromId, dsId, usesText, dashed: true);
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

            foreach (var (dsId, _) in dataStoreIds)
                Arrow("ep", dsId, FormatDataStoreUsesText(ep));

            foreach (var evt in events)
                Arrow("ep", "bus", $"publishes {MermaidDiagramText.ShortTypeName(evt)}", dashed: true);
        }

        var source = string.Join("\n", lines);
        var kindLabel = SystemViewFilter.KindLabel(ep.Kind);
        var projPrefix = string.IsNullOrWhiteSpace(ep.ProjectName) ? "" : $"[{ep.ProjectName}] ";
        var caption = $"{projPrefix}{kindLabel} sequence for {opTitle}: " +
                      BuildCaptionSummary(serviceIds.Count, contextIds.Count, dataStoreIds.Count, events.Count);

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

        return FormatDataStoreUsesText(ep);
    }

    private static string FormatDataStoreUsesText(SystemEntrypointInfo ep)
    {
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

    private static string ServiceImplName(string rawService)
    {
        var arrow = rawService.IndexOf(" -> ", StringComparison.Ordinal);
        return arrow >= 0 ? rawService[(arrow + 4)..].Trim() : rawService.Trim();
    }

    private static string BuildCaptionSummary(int services, int dbContexts, int dataStores, int events)
    {
        var parts = new List<string>();
        if (services > 0)
            parts.Add(MermaidDiagramText.CountText(services, "service"));
        if (dbContexts > 0)
            parts.Add(MermaidDiagramText.CountText(dbContexts, "DbContext"));
        if (dataStores > 0)
            parts.Add(MermaidDiagramText.CountText(dataStores, "data store"));
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
