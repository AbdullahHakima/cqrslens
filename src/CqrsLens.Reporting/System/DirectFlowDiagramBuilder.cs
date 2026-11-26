using System.Text;
using static CqrsLens.MermaidDiagramText;

namespace CqrsLens;

/// <summary>
/// Builds bounded per-project Mermaid <c>flowchart LR</c> diagrams for direct non-CQRS
/// <see cref="SystemEntrypointInfo"/> flows (<c>Direct</c>, <c>Worker</c>, <c>EventConsumer</c>):
/// Entrypoint / gRPC / Worker / Consumer → Services → DbContext / IntegrationEvent.
/// Emits only <see cref="FlowchartParser"/>-compatible syntax for zero-JS SVG rendering.
/// </summary>
internal static class DirectFlowDiagramBuilder
{
    internal const int MaxEntrypointsPerProject = 15;
    internal const int MaxEdgesPerProject = 40;
    private const int MaxServicesPerEntrypoint = 5;
    private const int MaxDbContextsPerEntrypoint = 4;
    private const int MaxEventsPerEntrypoint = 4;
    private const int MaxLabelLength = 48;

    internal static IReadOnlyList<DirectFlowDiagramSection> Build(
        AnalysisResult result,
        string? focusService = null,
        bool excludeTests = false)
    {
        ArgumentNullException.ThrowIfNull(result);

        var eligible = SystemViewFilter.VisibleEntrypoints(result, focusService, excludeTests)
            .Where(IsEligibleDirectEntrypoint)
            .ToList();
        if (eligible.Count == 0)
            return Array.Empty<DirectFlowDiagramSection>();

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

        var groups = eligible
            .GroupBy(e => string.IsNullOrWhiteSpace(e.ProjectName) ? UnassignedProject : e.ProjectName!)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        var sections = new List<DirectFlowDiagramSection>();
        foreach (var group in groups)
        {
            var ranked = group
                .OrderByDescending(RichnessScore)
                .ThenBy(e => e.Kind)
                .ThenBy(e => e.DisplayLabel, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.Name, StringComparer.Ordinal)
                .ToList();

            var capped = ranked.Take(MaxEntrypointsPerProject).ToList();
            var section = BuildBoundedProjectSection(
                group.Key, capped, ranked.Count, resolvedDbByService, resolvedStoreByService);
            if (section is not null)
                sections.Add(section);
        }

        return sections;
    }

    internal static bool IsEligibleDirectEntrypoint(SystemEntrypointInfo entrypoint)
        => entrypoint.ExecutionStyle != SystemExecutionStyle.Cqrs &&
           (entrypoint.CalledServices.Count > 0 ||
            entrypoint.TouchedDbContexts.Count > 0 ||
            entrypoint.TouchedDataStores.Count > 0 ||
            entrypoint.PublishedMessages.Count > 0);

    internal static int RichnessScore(SystemEntrypointInfo entrypoint)
        => entrypoint.CalledServices.Count +
           entrypoint.TouchedDbContexts.Count * 2 +
           entrypoint.TouchedDataStores.Count * 2 +
           entrypoint.PublishedMessages.Count * 2 +
           entrypoint.TouchedEntities.Count;

    private static DirectFlowDiagramSection? BuildBoundedProjectSection(
        string projectName,
        List<SystemEntrypointInfo> candidates,
        int totalInProject,
        IReadOnlyDictionary<string, HashSet<string>> resolvedDbByService,
        IReadOnlyDictionary<string, HashSet<string>> resolvedStoreByService)
    {
        for (var count = candidates.Count; count >= 1; count--)
        {
            var slice = candidates.Take(count).ToList();
            var (source, usedEntrypoints, edgeCount) = ComposeProject(
                projectName, slice, resolvedDbByService, resolvedStoreByService);
            if (edgeCount == 0 || usedEntrypoints == 0)
                return null;

            if (IsWithinMermaidLimits(source))
            {
                return new DirectFlowDiagramSection(
                    projectName,
                    source,
                    usedEntrypoints,
                    edgeCount,
                    totalInProject);
            }
        }

        return null;
    }

    private static (string Source, int UsedEntrypoints, int EdgeCount) ComposeProject(
        string projectName,
        IReadOnlyList<SystemEntrypointInfo> entrypoints,
        IReadOnlyDictionary<string, HashSet<string>> resolvedDbByService,
        IReadOnlyDictionary<string, HashSet<string>> resolvedStoreByService)
    {
        var ids = new MermaidIdAllocator();
        var allServices = entrypoints.SelectMany(e => e.CalledServices).Distinct(StringComparer.Ordinal).ToList();
        var allContexts = entrypoints.SelectMany(e => e.TouchedDbContexts).Distinct(StringComparer.Ordinal).ToList();
        var allEvents = entrypoints.SelectMany(e => e.PublishedMessages).Distinct(StringComparer.Ordinal).ToList();

        var nodes = new List<(string Id, string Label, string ClassName, bool IsStadium)>();
        var nodeSeen = new HashSet<string>(StringComparer.Ordinal);
        var edges = new List<(string From, string To, string Label, bool Dotted)>();
        var edgeSeen = new HashSet<string>(StringComparer.Ordinal);
        var usedEntrypoints = 0;

        string EnsureNode(string key, string prefix, string label, string className, bool isStadium)
        {
            var id = ids.Allocate(key, prefix, label);
            if (nodeSeen.Add(id))
                nodes.Add((id, label, className, isStadium));
            return id;
        }

        void AddEdge(string from, string to, string label, bool dotted)
        {
            var key = $"{from}|{to}|{label}|{dotted}";
            if (edgeSeen.Add(key))
                edges.Add((from, to, label, dotted));
        }

        foreach (var ep in entrypoints)
        {
            var plannedEdges = new List<(string FromKey, string FromPrefix, string FromLabel, string FromClass, bool FromStadium,
                string ToKey, string ToPrefix, string ToLabel, string ToClass, bool ToStadium,
                string EdgeLabel, bool Dotted)>();

            var epKey = $"ep|{ep.Kind}|{ep.Method}|{ep.RouteOrTrigger}|{ep.Name}";
            var epLabel = FormatEntrypointLabel(ep);
            var epPrefix = EntrypointPrefix(ep.Kind);
            var epClass = EntrypointClass(ep.Kind);

            var services = ep.CalledServices.Take(MaxServicesPerEntrypoint).ToList();
            var dbContexts = ep.TouchedDbContexts.Take(MaxDbContextsPerEntrypoint).ToList();
            var shortContextNames = dbContexts.Select(ShortTypeName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var dataStores = ep.TouchedDataStores
                .Where(ds => !shortContextNames.Contains(ds))
                .Take(MaxDbContextsPerEntrypoint)
                .ToList();
            var events = ep.PublishedMessages.Take(MaxEventsPerEntrypoint).ToList();

            foreach (var svc in services)
            {
                var svcLabel = TruncateLabel(ShortTypeName(svc, allServices), MaxLabelLength);
                plannedEdges.Add((
                    epKey, epPrefix, epLabel, epClass, true,
                    "svc|" + svc, "svc", svcLabel, "clsDirectService", false,
                    string.Empty, false));
            }

            if (services.Count > 0)
            {
                var primaryDbService = services.FirstOrDefault(s => !LooksLikeBusOrMessaging(s)) ?? services[0];
                var primaryPubService = services.FirstOrDefault(LooksLikeBusOrMessaging) ?? services[0];

                foreach (var ctx in dbContexts)
                {
                    var dbOwner = services.FirstOrDefault(s =>
                        resolvedDbByService.TryGetValue(s, out var set) && set.Contains(ctx))
                        ?? primaryDbService;
                    var ownerLabel = TruncateLabel(ShortTypeName(dbOwner, allServices), MaxLabelLength);
                    var ctxLabel = TruncateLabel(ShortTypeName(ctx, allContexts), MaxLabelLength);
                    plannedEdges.Add((
                        "svc|" + dbOwner, "svc", ownerLabel, "clsDirectService", false,
                        "dbc|" + ctx, "dbc", ctxLabel, "clsDirectDbContext", false,
                        string.Empty, false));
                }

                foreach (var ds in dataStores)
                {
                    var dsOwner = services.FirstOrDefault(s =>
                        resolvedStoreByService.TryGetValue(ServiceImplName(s), out var set) && set.Contains(ds))
                        ?? primaryDbService;
                    var ownerLabel = TruncateLabel(ShortTypeName(dsOwner, allServices), MaxLabelLength);
                    var dsLabel = TruncateLabel(ds, MaxLabelLength);
                    plannedEdges.Add((
                        "svc|" + dsOwner, "svc", ownerLabel, "clsDirectService", false,
                        "ds|" + ds, "ds", dsLabel, "clsDirectDbContext", false,
                        string.Empty, false));
                }

                foreach (var evt in events)
                {
                    var pubLabel = TruncateLabel(ShortTypeName(primaryPubService, allServices), MaxLabelLength);
                    var evtLabel = TruncateLabel(ShortTypeName(evt, allEvents), MaxLabelLength);
                    plannedEdges.Add((
                        "svc|" + primaryPubService, "svc", pubLabel, "clsDirectService", false,
                        "evt|" + evt, "evt", evtLabel, "clsDirectEvent", false,
                        "publishes", true));
                }
            }
            else
            {
                foreach (var ctx in dbContexts)
                {
                    var ctxLabel = TruncateLabel(ShortTypeName(ctx, allContexts), MaxLabelLength);
                    plannedEdges.Add((
                        epKey, epPrefix, epLabel, epClass, true,
                        "dbc|" + ctx, "dbc", ctxLabel, "clsDirectDbContext", false,
                        string.Empty, false));
                }

                foreach (var ds in dataStores)
                {
                    var dsLabel = TruncateLabel(ds, MaxLabelLength);
                    plannedEdges.Add((
                        epKey, epPrefix, epLabel, epClass, true,
                        "ds|" + ds, "ds", dsLabel, "clsDirectDbContext", false,
                        string.Empty, false));
                }

                foreach (var evt in events)
                {
                    var evtLabel = TruncateLabel(ShortTypeName(evt, allEvents), MaxLabelLength);
                    plannedEdges.Add((
                        epKey, epPrefix, epLabel, epClass, true,
                        "evt|" + evt, "evt", evtLabel, "clsDirectEvent", false,
                        "publishes", true));
                }
            }

            if (plannedEdges.Count == 0)
                continue;

            var newEdgeCount = CountDistinctNewEdges(plannedEdges, ids, edgeSeen);
            if (usedEntrypoints > 0 && edges.Count + newEdgeCount > MaxEdgesPerProject)
                break;

            foreach (var item in plannedEdges)
            {
                var fromId = EnsureNode(item.FromKey, item.FromPrefix, item.FromLabel, item.FromClass, item.FromStadium);
                var toId = EnsureNode(item.ToKey, item.ToPrefix, item.ToLabel, item.ToClass, item.ToStadium);
                AddEdge(fromId, toId, item.EdgeLabel, item.Dotted);
                if (edges.Count >= MaxEdgesPerProject)
                    break;
            }

            usedEntrypoints++;
            if (edges.Count >= MaxEdgesPerProject)
                break;
        }

        if (edges.Count == 0)
            return ("", 0, 0);

        var sgId = ids.Allocate("sg|" + projectName, "df", projectName);
        var sb = new StringBuilder();
        sb.AppendLine("flowchart LR");
        sb.AppendLine($"    accTitle: Direct flows — {MermaidLabel(projectName)}");
        sb.AppendLine($"    accDescr: {CountText(usedEntrypoints, "direct operation")}, {CountText(edges.Count, "edge")}.");
        sb.AppendLine($"    subgraph {sgId}[\"{MermaidLabel(projectName)}\"]");

        foreach (var (id, label, className, isStadium) in nodes)
        {
            var shape = isStadium
                ? $"([\"{MermaidLabel(label)}\"])"
                : $"[\"{MermaidLabel(label)}\"]";
            sb.AppendLine($"        {id}{shape}:::{className}");
        }

        sb.AppendLine("    end");

        var dottedIndices = new List<int>();
        for (var i = 0; i < edges.Count; i++)
        {
            var (from, to, label, dotted) = edges[i];
            var arrow = dotted ? "-.->" : "-->";
            var edgeText = string.IsNullOrWhiteSpace(label) ? "" : $"|{MermaidLabel(label)}|";
            sb.AppendLine($"    {from} {arrow}{edgeText} {to}");
            if (dotted)
                dottedIndices.Add(i);
        }

        sb.AppendLine($"    style {sgId} fill:none,stroke:#94a3b8,stroke-dasharray:4 3");
        foreach (var idx in dottedIndices)
            sb.AppendLine($"    linkStyle {idx} stroke:#7c3aed,stroke-width:1.5px,stroke-dasharray:4 3");

        sb.AppendLine("    classDef clsDirectEndpoint fill:#e0f2fe,stroke:#0284c7,stroke-width:2px,color:#0c4a6e");
        sb.AppendLine("    classDef clsDirectGrpc fill:#eef2ff,stroke:#4f46e5,stroke-width:2px,color:#312e81");
        sb.AppendLine("    classDef clsDirectWorker fill:#fff7ed,stroke:#c2410c,stroke-width:2px,color:#431407");
        sb.AppendLine("    classDef clsDirectConsumer fill:#fdf4ff,stroke:#a21caf,stroke-width:2px,color:#4a044e");
        sb.AppendLine("    classDef clsDirectService fill:#f8fafc,stroke:#475569,stroke-width:1.5px,color:#1e293b");
        sb.AppendLine("    classDef clsDirectDbContext fill:#ecfdf5,stroke:#059669,stroke-width:2px,color:#064e3b");
        sb.AppendLine("    classDef clsDirectEvent fill:#fefce8,stroke:#ca8a04,stroke-width:2px,color:#422006");

        return (sb.ToString().TrimEnd(), usedEntrypoints, edges.Count);
    }

    private static int CountDistinctNewEdges(
        List<(string FromKey, string FromPrefix, string FromLabel, string FromClass, bool FromStadium,
            string ToKey, string ToPrefix, string ToLabel, string ToClass, bool ToStadium,
            string EdgeLabel, bool Dotted)> planned,
        MermaidIdAllocator ids,
        HashSet<string> existing)
    {
        var added = 0;
        var tempSeen = new HashSet<string>(existing, StringComparer.Ordinal);
        foreach (var item in planned)
        {
            var fromId = ids.Allocate(item.FromKey, item.FromPrefix, item.FromLabel);
            var toId = ids.Allocate(item.ToKey, item.ToPrefix, item.ToLabel);
            if (tempSeen.Add($"{fromId}|{toId}|{item.EdgeLabel}|{item.Dotted}"))
                added++;
        }
        return added;
    }

    private static string FormatEntrypointLabel(SystemEntrypointInfo ep)
    {
        var raw = ep.Kind switch
        {
            SystemEntrypointKind.Grpc => "gRPC " + KeepClassAndMember(string.IsNullOrWhiteSpace(ep.RouteOrTrigger) ? ep.Name : ep.RouteOrTrigger),
            SystemEntrypointKind.Worker => "Worker " + KeepClassAndMember(string.IsNullOrWhiteSpace(ep.RouteOrTrigger) ? ep.Name : ep.RouteOrTrigger),
            SystemEntrypointKind.MessageConsumer => "Consumer " + ShortTypeName(string.IsNullOrWhiteSpace(ep.RouteOrTrigger) ? ep.Name : ep.RouteOrTrigger),
            _ => ep.DisplayLabel,
        };
        return TruncateLabel(raw, MaxLabelLength);
    }

    internal static string KeepClassAndMember(string value)
    {
        var trimmed = value.Trim();
        var lastDot = trimmed.LastIndexOf('.');
        if (lastDot <= 0)
            return trimmed;

        var prevDot = trimmed.LastIndexOf('.', lastDot - 1);
        return trimmed[(prevDot + 1)..];
    }

    internal static string TruncateLabel(string value, int maxLength)
        => value.Length <= maxLength
            ? value
            : string.Concat(value.AsSpan(0, maxLength - 1).TrimEnd(), "…");

    private static string ServiceImplName(string rawService)
    {
        var arrow = rawService.IndexOf(" -> ", StringComparison.Ordinal);
        return arrow >= 0 ? rawService[(arrow + 4)..].Trim() : rawService.Trim();
    }

    private static bool LooksLikeBusOrMessaging(string serviceName)
        => serviceName.Contains("EventBus", StringComparison.OrdinalIgnoreCase) ||
           serviceName.Contains("IntegrationEvent", StringComparison.OrdinalIgnoreCase) ||
           serviceName.Contains("Publisher", StringComparison.OrdinalIgnoreCase) ||
           serviceName.Contains("MessageBus", StringComparison.OrdinalIgnoreCase);

    private static string EntrypointPrefix(SystemEntrypointKind kind) => kind switch
    {
        SystemEntrypointKind.Grpc => "grpc",
        SystemEntrypointKind.Worker => "wrk",
        SystemEntrypointKind.MessageConsumer => "csm",
        _ => "ep",
    };

    private static string EntrypointClass(SystemEntrypointKind kind) => kind switch
    {
        SystemEntrypointKind.Grpc => "clsDirectGrpc",
        SystemEntrypointKind.Worker => "clsDirectWorker",
        SystemEntrypointKind.MessageConsumer => "clsDirectConsumer",
        _ => "clsDirectEndpoint",
    };
}
