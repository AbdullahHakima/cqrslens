using System.Text;

namespace CqrsLens;

/// <summary>
/// Builds the C4-style System &amp; Container Landscape (<c>flowchart LR</c>): services grouped
/// by runtime role, inferred data stores / message brokers, integration flows, and
/// service → data edges. Uses only the syntax the zero-JS SVG renderer understands.
/// </summary>
internal static class SystemLandscapeDiagramBuilder
{
    private const int MaxDataNodes = 30;
    private const int MaxEdgeLabelLength = 36;

    private static readonly string[] BrokerTechnologies =
    {
        "RabbitMQ", "Kafka", "Azure Service Bus", "MassTransit", "NServiceBus",
    };

    private static readonly string[] DatabaseTechnologies =
    {
        "PostgreSQL", "SQL Server", "SQLite", "MongoDB", "MySQL", "Cosmos DB", "Oracle",
    };

    private static readonly (string Key, string Label)[] Groups =
    {
        ("ui", "UI / Clients"),
        ("api", "APIs & Services"),
        ("worker", "Workers & Processors"),
        ("orchestrator", "Orchestrators"),
        ("data", "Data & Messaging"),
    };

    /// <summary>
    /// Returns the landscape source, or <see langword="null"/> when the analysis holds no
    /// system data (no service profiles, system entrypoints or integration flows) or no
    /// non-test service survives filtering.
    /// </summary>
    internal static string? Build(AnalysisResult result, string? focusService = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.ServiceProfiles.Count == 0 &&
            result.SystemEntrypoints.Count == 0 &&
            result.IntegrationFlows.Count == 0)
            return null;

        var full = Compose(result, focusService, includeData: true);
        if (full is null || MermaidDiagramText.IsWithinMermaidLimits(full))
            return full;

        // Too large: drop the inferred data layer before giving up (callers emit a limit note).
        return Compose(result, focusService, includeData: false) ?? full;
    }

    private static string? Compose(AnalysisResult result, string? focusService, bool includeData)
    {
        var knownProjects = new HashSet<string>(
            SystemViewFilter.KnownProjectNames(result), StringComparer.OrdinalIgnoreCase);

        // ---- Services --------------------------------------------------------------------
        var services = new Dictionary<string, ServiceRuntimeRole>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in result.ServiceProfiles)
        {
            if (profile.RuntimeRole == ServiceRuntimeRole.Tests)
                continue;
            if (profile.RuntimeRole != ServiceRuntimeRole.Library ||
                result.SystemEntrypoints.Any(e => SystemViewFilter.SameProject(e.ProjectName, profile.ProjectName)) ||
                result.IntegrationFlows.Any(f => SystemViewFilter.SameProject(f.FromProject, profile.ProjectName) ||
                                                 SystemViewFilter.SameProject(f.ToProject, profile.ProjectName)))
            {
                services[profile.ProjectName] = profile.RuntimeRole;
            }
        }

        foreach (var entrypoint in result.SystemEntrypoints)
        {
            if (!string.IsNullOrWhiteSpace(entrypoint.ProjectName) &&
                !SystemViewFilter.IsTestProject(result, entrypoint.ProjectName))
            {
                services.TryAdd(entrypoint.ProjectName, ServiceRuntimeRole.Api);
            }
        }

        foreach (var flow in result.IntegrationFlows)
        {
            foreach (var end in new[] { flow.FromProject, flow.ToProject })
            {
                if (!string.IsNullOrWhiteSpace(end) && knownProjects.Contains(end) &&
                    !SystemViewFilter.IsTestProject(result, end))
                {
                    services.TryAdd(end, SystemViewFilter.FindProfile(result, end)?.RuntimeRole switch
                    {
                        ServiceRuntimeRole.Worker => ServiceRuntimeRole.Worker,
                        ServiceRuntimeRole.Ui => ServiceRuntimeRole.Ui,
                        ServiceRuntimeRole.Orchestrator => ServiceRuntimeRole.Orchestrator,
                        _ => ServiceRuntimeRole.Api,
                    });
                }
            }
        }

        // ---- Focus -------------------------------------------------------------------------
        var focused = SystemViewFilter.ResolveFocus(result, focusService);
        HashSet<string>? neighborhood = null;
        if (focused.Count > 0)
        {
            neighborhood = SystemViewFilter.FocusNeighborhood(result, focused);
            foreach (var name in services.Keys.ToList())
            {
                if (!neighborhood.Contains(name))
                    services.Remove(name);
            }
        }

        // ---- Flows between visible ends --------------------------------------------------
        var flows = result.IntegrationFlows
            .Where(f => !SystemViewFilter.IsTestProject(result, f.FromProject) &&
                        !SystemViewFilter.IsTestProject(result, f.ToProject))
            .Where(f => focused.Count == 0 || focused.Contains(f.FromProject) || focused.Contains(f.ToProject))
            .Where(f => services.ContainsKey(f.FromProject))
            .Where(f => services.ContainsKey(f.ToProject) || (includeData && !knownProjects.Contains(f.ToProject)))
            .ToList();

        // ---- Data & messaging nodes --------------------------------------------------------
        var dataNodes = new List<(string Key, string Label, bool IsBroker)>();
        var dataEdges = new List<(string Service, string DataKey, string? Label)>();
        string? busKey = null;
        if (includeData)
            CollectData(result, services, flows, knownProjects, focused.Count > 0, dataNodes, dataEdges, out busKey);
        var dataKeys = new HashSet<string>(dataNodes.Select(n => n.Key), StringComparer.OrdinalIgnoreCase);

        if (services.Count == 0)
            return null;

        // ---- Emit ---------------------------------------------------------------------------
        var ids = new MermaidIdAllocator();
        string ServiceId(string name) => ids.Allocate("svc:" + name.ToUpperInvariant(), "svc", name);
        string DataId(string key) => ids.Allocate("data:" + key, "data", key);

        var sb = new StringBuilder();
        sb.AppendLine("flowchart LR");
        sb.AppendLine("    accTitle: System landscape");
        sb.AppendLine(
            $"    accDescr: {MermaidDiagramText.CountText(services.Count, "service")}, " +
            $"{MermaidDiagramText.CountText(dataNodes.Count, "data or messaging node")}, " +
            $"{MermaidDiagramText.CountText(flows.Count, "integration flow")}.");

        var groupIds = new List<string>();
        foreach (var (key, label) in Groups)
        {
            var members = key == "data"
                ? new List<string>()
                : services
                    .Where(s => GroupKey(s.Value) == key)
                    .Select(s => s.Key)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            if (key == "data" ? dataNodes.Count == 0 : members.Count == 0)
                continue;

            var groupId = ids.Allocate("group:" + key, "grp", key);
            groupIds.Add(groupId);
            sb.AppendLine($"    subgraph {groupId}[\"{MermaidDiagramText.MermaidLabel(label)}\"]");
            if (key == "data")
            {
                foreach (var node in dataNodes)
                {
                    var shape = node.IsBroker
                        ? $"([\"{MermaidDiagramText.MermaidLabel(node.Label)}\"])"
                        : $"[\"{MermaidDiagramText.MermaidLabel(node.Label)}\"]";
                    sb.AppendLine($"        {DataId(node.Key)}{shape}:::{(node.IsBroker ? "clsBroker" : "clsData")}");
                }
            }
            else
            {
                foreach (var name in members)
                {
                    var cls = focused.Contains(name) ? "clsFocus" : RoleClass(services[name]);
                    sb.AppendLine(
                        $"        {ServiceId(name)}[\"{MermaidDiagramText.MermaidLabel(ServiceLabel(result, name, services[name]))}\"]:::{cls}");
                }
            }
            sb.AppendLine("    end");
        }

        var edgeCount = 0;
        var dottedEdges = new List<int>();
        var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Edge(string from, string to, string? label, bool dotted)
        {
            var key = $"{from}|{to}|{label}|{dotted}";
            if (!emitted.Add(key))
                return;
            var arrow = dotted ? "-.->" : "-->";
            var text = string.IsNullOrWhiteSpace(label) ? "" : $"|{EdgeLabel(label)}|";
            sb.AppendLine($"    {from} {arrow}{text} {to}");
            if (dotted)
                dottedEdges.Add(edgeCount);
            edgeCount++;
        }

        // Integration events through the broker hub when one exists, else direct.
        var eventFlows = flows.Where(f => f.Kind == IntegrationFlowKind.IntegrationEvent).ToList();
        if (busKey is not null && eventFlows.Count > 0)
        {
            foreach (var publisher in eventFlows.GroupBy(f => f.FromProject, StringComparer.OrdinalIgnoreCase)
                         .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
                Edge(ServiceId(publisher.Key), DataId(busKey), EventsLabel(publisher, "publishes"), dotted: false);
            foreach (var subscriber in eventFlows.Where(f => services.ContainsKey(f.ToProject))
                         .GroupBy(f => f.ToProject, StringComparer.OrdinalIgnoreCase)
                         .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
                Edge(DataId(busKey), ServiceId(subscriber.Key), EventsLabel(subscriber, "delivers"), dotted: true);
        }
        else
        {
            foreach (var pair in eventFlows.Where(f => services.ContainsKey(f.ToProject))
                         .GroupBy(f => (From: f.FromProject.ToUpperInvariant(), To: f.ToProject.ToUpperInvariant()))
                         .OrderBy(g => g.Key.From, StringComparer.Ordinal)
                         .ThenBy(g => g.Key.To, StringComparer.Ordinal))
            {
                var first = pair.First();
                Edge(ServiceId(first.FromProject), ServiceId(first.ToProject), EventsLabel(pair, "event"), dotted: true);
            }
        }

        foreach (var flow in flows.Where(f => f.Kind != IntegrationFlowKind.IntegrationEvent)
                     .OrderBy(f => f.FromProject, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(f => f.ToProject, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(f => f.Kind))
        {
            string target;
            if (services.ContainsKey(flow.ToProject))
                target = ServiceId(flow.ToProject);
            else if (dataKeys.Contains(InfraKey(flow.ToProject)))
                target = DataId(InfraKey(flow.ToProject));
            else
                continue;
            var (label, dotted) = flow.Kind switch
            {
                IntegrationFlowKind.HttpClient => ("HTTP", false),
                IntegrationFlowKind.GrpcClient => ("gRPC", false),
                IntegrationFlowKind.SharedDbContext => ("shares " + flow.Label, true),
                _ => ("references", true),
            };
            Edge(ServiceId(flow.FromProject), target, label, dotted);
        }

        foreach (var (service, dataKey, label) in dataEdges
                     .OrderBy(e => e.Service, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(e => e.DataKey, StringComparer.Ordinal))
        {
            if (services.ContainsKey(service))
                Edge(ServiceId(service), DataId(dataKey), label, dotted: false);
        }

        foreach (var groupId in groupIds)
            sb.AppendLine($"    style {groupId} fill:none,stroke:#94a3b8,stroke-dasharray:4 3");
        foreach (var index in dottedEdges)
            sb.AppendLine($"    linkStyle {index} stroke:#7c3aed,stroke-width:1.5px,stroke-dasharray:4 3");

        sb.AppendLine("    classDef clsUi fill:#fdf4ff,stroke:#a21caf,stroke-width:2px,color:#4a044e");
        sb.AppendLine("    classDef clsApi fill:#eff6ff,stroke:#2563eb,stroke-width:2px,color:#1e3a8a");
        sb.AppendLine("    classDef clsWorker fill:#fff7ed,stroke:#c2410c,stroke-width:2px,color:#431407");
        sb.AppendLine("    classDef clsOrchestrator fill:#f5f3ff,stroke:#7c3aed,stroke-width:2px,color:#2e1065");
        sb.AppendLine("    classDef clsData fill:#ecfdf5,stroke:#059669,stroke-width:2px,color:#064e3b");
        sb.AppendLine("    classDef clsBroker fill:#fefce8,stroke:#ca8a04,stroke-width:2px,color:#422006");
        sb.AppendLine("    classDef clsFocus fill:#fee2e2,stroke:#dc2626,stroke-width:3px,color:#450a0a");
        return sb.ToString().TrimEnd();
    }

    private static void CollectData(
        AnalysisResult result,
        IReadOnlyDictionary<string, ServiceRuntimeRole> services,
        IReadOnlyList<IntegrationFlowEdge> flows,
        IReadOnlySet<string> knownProjects,
        bool focusedView,
        List<(string Key, string Label, bool IsBroker)> dataNodes,
        List<(string Service, string DataKey, string? Label)> dataEdges,
        out string? busKey)
    {
        busKey = null;
        var nodeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        bool AddNode(string key, string label, bool isBroker)
        {
            if (nodeKeys.Contains(key))
                return true;
            if (dataNodes.Count >= MaxDataNodes)
                return false;
            nodeKeys.Add(key);
            dataNodes.Add((key, label, isBroker));
            return true;
        }

        // Message broker: detected technologies, or integration events with no explicit broker.
        var brokerTechs = result.ServiceProfiles
            .Where(p => services.ContainsKey(p.ProjectName))
            .SelectMany(p => p.Technologies)
            .Where(t => BrokerTechnologies.Contains(t, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var hasEvents = flows.Any(f => f.Kind == IntegrationFlowKind.IntegrationEvent);
        if (brokerTechs.Count > 0 || hasEvents)
        {
            var label = brokerTechs.Count > 0 ? $"Event Bus · {string.Join(" / ", brokerTechs)}" : "Event Bus";
            if (AddNode("bus", label, isBroker: true))
                busKey = "bus";
        }

        // Aspire infrastructure resources referenced by services.
        foreach (var flow in flows.Where(f => !knownProjects.Contains(f.ToProject))
                     .OrderBy(f => f.ToProject, StringComparer.OrdinalIgnoreCase))
        {
            var isBroker = BrokerTechnologies.Any(t => flow.ToProject.Contains(t, StringComparison.OrdinalIgnoreCase));
            AddNode(InfraKey(flow.ToProject), flow.ToProject, isBroker);
        }

        // Redis caches.
        var redisCachedServices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in result.ServiceProfiles
                     .Where(p => services.ContainsKey(p.ProjectName) &&
                                 p.Technologies.Contains("Redis", StringComparer.OrdinalIgnoreCase))
                     .OrderBy(p => p.ProjectName, StringComparer.OrdinalIgnoreCase))
        {
            if (AddNode("cache", "Cache · Redis", isBroker: false))
            {
                dataEdges.Add((profile.ProjectName, "cache", "caches"));
                redisCachedServices.Add(profile.ProjectName);
            }
        }

        // DbContexts → services that touch them (entrypoint tracing), else owner / dependents.
        var touchedBy = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var storeTouchedBy = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var entrypoint in result.SystemEntrypoints)
        {
            if (string.IsNullOrWhiteSpace(entrypoint.ProjectName) || !services.ContainsKey(entrypoint.ProjectName))
                continue;
            foreach (var context in entrypoint.TouchedDbContexts)
            {
                var shortName = MermaidDiagramText.ShortTypeName(context);
                if (!touchedBy.TryGetValue(shortName, out var set))
                    touchedBy[shortName] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                set.Add(entrypoint.ProjectName);
            }
            foreach (var ds in entrypoint.TouchedDataStores)
            {
                if (!storeTouchedBy.TryGetValue(ds, out var dsSet))
                    storeTouchedBy[ds] = dsSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                dsSet.Add(entrypoint.ProjectName);
            }
        }

        foreach (var context in result.DbContexts
                     .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(c => c.FullName, StringComparer.Ordinal))
        {
            var key = "db_" + context.Name;
            var users = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (touchedBy.TryGetValue(context.Name, out var traced))
                users.UnionWith(traced);
            if (users.Count == 0 && context.ProjectName is not null)
            {
                if (services.ContainsKey(context.ProjectName))
                {
                    users.Add(context.ProjectName);
                }
                else
                {
                    foreach (var project in result.Projects)
                    {
                        if (services.ContainsKey(project.Name) &&
                            project.ReferenceNames.Contains(context.ProjectName, StringComparer.OrdinalIgnoreCase))
                            users.Add(project.Name);
                    }
                }
            }

            if (users.Count == 0 && focusedView)
                continue; // focused view: skip stores unrelated to the visible services

            if (!AddNode(key, DbLabel(result, context), isBroker: false))
                break;
            foreach (var user in users)
                dataEdges.Add((user, key, null));
        }

        // Non-EF DataStores (Linq2Db, Dapper, MongoDB, NHibernate, ADO.NET, Cosmos DB, Marten, RavenDB, Redis).
        foreach (var store in result.DataStores
                     .Where(d => d.Kind is not DataStoreKind.EfCore and not DataStoreKind.Ef6)
                     .OrderBy(d => d.Kind)
                     .ThenBy(d => d.Name, StringComparer.Ordinal))
        {
            if (store.Kind == DataStoreKind.Redis &&
                store.ProjectName is not null &&
                redisCachedServices.Contains(store.ProjectName))
            {
                continue;
            }

            var users = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (storeTouchedBy.TryGetValue(store.Name, out var tracedStoreUsers))
                users.UnionWith(tracedStoreUsers);

            foreach (var effect in result.ServiceClassEffects.Values)
            {
                if (!string.IsNullOrWhiteSpace(effect.ProjectName) &&
                    services.ContainsKey(effect.ProjectName) &&
                    effect.TouchedDataStores.Contains(store.Name, StringComparer.Ordinal))
                {
                    users.Add(effect.ProjectName);
                }
            }

            if (store.ProjectName is not null && services.ContainsKey(store.ProjectName))
                users.Add(store.ProjectName);
            else if (users.Count == 0 && store.ProjectName is not null)
            {
                foreach (var project in result.Projects)
                {
                    if (services.ContainsKey(project.Name) &&
                        project.ReferenceNames.Contains(store.ProjectName, StringComparer.OrdinalIgnoreCase))
                        users.Add(project.Name);
                }
            }

            if (users.Count == 0 && focusedView)
                continue;

            var key = $"ds_{store.Kind}_{store.Name}";
            var label = !string.IsNullOrWhiteSpace(store.Provider) &&
                        !store.Name.Contains(store.Provider, StringComparison.OrdinalIgnoreCase)
                ? $"{store.Name} · {store.Provider}"
                : store.Name;
            if (!AddNode(key, label, isBroker: false))
                break;
            foreach (var user in users)
                dataEdges.Add((user, key, null));
        }
    }

    private static string DbLabel(AnalysisResult result, DbContextInfo context)
    {
        var owner = SystemViewFilter.FindProfile(result, context.ProjectName);
        var tech = owner?.Technologies
            .Where(t => DatabaseTechnologies.Contains(t, StringComparer.OrdinalIgnoreCase))
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();
        return tech.Count > 0 ? $"{context.Name} · {string.Join(" / ", tech)}" : $"{context.Name} · DbContext";
    }

    private static string ServiceLabel(AnalysisResult result, string name, ServiceRuntimeRole role)
    {
        var profile = SystemViewFilter.FindProfile(result, name);
        var entrypointCount = result.SystemEntrypoints.Count(e =>
            SystemViewFilter.SameProject(e.ProjectName, name) &&
            e.Kind is SystemEntrypointKind.Http or SystemEntrypointKind.Grpc or
                      SystemEntrypointKind.Blazor or SystemEntrypointKind.RazorPage);
        var profileEndpoints = profile is null ? 0 : profile.HttpEndpointCount + profile.GrpcEndpointCount;
        var endpoints = Math.Max(entrypointCount, profileEndpoints);
        var processors = profile is not null
            ? profile.WorkerCount + profile.MessageConsumerCount
            : result.SystemEntrypoints.Count(e =>
                SystemViewFilter.SameProject(e.ProjectName, name) &&
                e.Kind is SystemEntrypointKind.Worker or SystemEntrypointKind.MessageConsumer);
        var detail = role switch
        {
            ServiceRuntimeRole.Worker => MermaidDiagramText.CountText(processors, "processor"),
            ServiceRuntimeRole.Orchestrator => "orchestrator",
            _ => MermaidDiagramText.CountText(endpoints, "endpoint"),
        };
        return $"{name} · {SystemViewFilter.RoleLabel(role)} · {detail}";
    }

    private static string GroupKey(ServiceRuntimeRole role) => role switch
    {
        ServiceRuntimeRole.Ui => "ui",
        ServiceRuntimeRole.Worker => "worker",
        ServiceRuntimeRole.Orchestrator => "orchestrator",
        _ => "api",
    };

    private static string RoleClass(ServiceRuntimeRole role) => role switch
    {
        ServiceRuntimeRole.Ui => "clsUi",
        ServiceRuntimeRole.Worker => "clsWorker",
        ServiceRuntimeRole.Orchestrator => "clsOrchestrator",
        _ => "clsApi",
    };

    private static string InfraKey(string name) => "infra_" + name;

    private static string EventsLabel(IEnumerable<IntegrationFlowEdge> flows, string verb)
    {
        var events = flows
            .Select(f => f.MessageType ?? f.Label)
            .Select(e => MermaidDiagramText.ShortTypeName(e))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(e => e, StringComparer.Ordinal)
            .ToList();
        return events.Count == 1 ? events[0] : $"{verb} {events.Count} events";
    }

    /// <summary>Edge labels stay plain words: no characters that could close the pipe syntax.</summary>
    private static string EdgeLabel(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
            sb.Append(char.IsLetterOrDigit(c) || c is '.' or '_' or '-' or ' ' or '/' ? c : ' ');
        var text = string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (text.Length > MaxEdgeLabelLength)
            text = string.Concat(text.AsSpan(0, MaxEdgeLabelLength - 1).TrimEnd(), "…");
        return text.Length == 0 ? "flow" : MermaidDiagramText.MermaidLabel(text);
    }
}
