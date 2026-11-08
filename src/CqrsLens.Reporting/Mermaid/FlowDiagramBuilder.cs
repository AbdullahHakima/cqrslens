using System.Text;
using static CqrsLens.MermaidDiagramText;

namespace CqrsLens;

/// <summary>
/// Builds a Mermaid <c>flowchart LR</c> for a set of Phase 4 flow edges:
/// endpoint → request → handler → DbContext, plus handler → request chains and
/// handler → notification publishes. Edges come straight from the model; no edge
/// is derived here.
/// </summary>
internal static class FlowDiagramBuilder
{
    internal static string BuildSource(
        IReadOnlyList<FlowEdge> edges,
        IReadOnlyList<ResolvedEdge> resolvedEdges,
        string? subgraphLabel)
    {
        var ordered = edges
            .GroupBy(e => (e.Kind, e.From, e.To))
            .Select(g => g.First())
            .OrderBy(e => e.Kind)
            .ThenBy(e => e.From, StringComparer.Ordinal)
            .ThenBy(e => e.To, StringComparer.Ordinal)
            .ToList();

        var orderedResolved = resolvedEdges
            .GroupBy(e => (e.Kind, e.From, e.Via, e.To))
            .Select(g => g.First())
            .OrderBy(e => e.From, StringComparer.Ordinal)
            .ThenBy(e => e.Via, StringComparer.Ordinal)
            .ThenBy(e => e.To, StringComparer.Ordinal)
            .ToList();

        var ids = new MermaidIdAllocator();
        var nodeIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var nodeOrder = new List<(string Role, string Name)>();
        var roleNames = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        void Register(string role, string name)
        {
            var key = role + "|" + name;
            if (nodeIds.ContainsKey(key))
                return;

            nodeIds[key] = "";
            nodeOrder.Add((role, name));
            if (!roleNames.TryGetValue(role, out var names))
                roleNames[role] = names = new List<string>();
            names.Add(name);
        }

        foreach (var edge in ordered)
        {
            var (fromRole, toRole) = Roles(edge.Kind);
            Register(fromRole, edge.From);
            Register(toRole, edge.To);
        }

        foreach (var edge in orderedResolved)
        {
            var (fromRole, toRole) = Roles(edge.Kind);
            Register(fromRole, edge.From);
            Register(toRole, edge.To);
        }

        foreach (var (role, name) in nodeOrder)
        {
            var label = NodeLabel(role, name, roleNames[role]);
            nodeIds[role + "|" + name] = ids.Allocate(role + "|" + name, Prefix(role), label);
        }

        var description = new StringBuilder();
        description.Append(CountText(ordered.Count + orderedResolved.Count, "edge"));
        description.Append(", ");
        description.Append(CountText(nodeOrder.Count, "node"));
        description.Append('.');

        var sb = new StringBuilder();
        sb.AppendLine("flowchart LR");
        sb.AppendLine("    accTitle: Flow map");
        sb.AppendLine($"    accDescr: {description}");

        if (subgraphLabel is not null)
            sb.AppendLine($"    subgraph flow[\"{MermaidLabel(subgraphLabel)}\"]");

        foreach (var (role, name) in nodeOrder)
        {
            var id = nodeIds[role + "|" + name];
            var label = NodeLabel(role, name, roleNames[role]);
            var shape = role == "endpoint" ? $"([\"{MermaidLabel(label)}\"])" : $"[\"{MermaidLabel(label)}\"]";
            sb.AppendLine($"        {id}{shape}:::{ClassName(role)}");
        }

        if (subgraphLabel is not null)
            sb.AppendLine("    end");

        foreach (var edge in ordered)
        {
            var (fromRole, toRole) = Roles(edge.Kind);
            var fromId = nodeIds[fromRole + "|" + edge.From];
            var toId = nodeIds[toRole + "|" + edge.To];
            sb.AppendLine($"    {fromId} -->|{EdgeLabel(edge.Kind)}| {toId}");
        }

        // DI-resolved hops are dotted and name the interface they resolve through, so
        // they are visibly distinct from a direct edge.
        foreach (var edge in orderedResolved)
        {
            var (fromRole, toRole) = Roles(edge.Kind);
            var fromId = nodeIds[fromRole + "|" + edge.From];
            var toId = nodeIds[toRole + "|" + edge.To];
            sb.AppendLine($"    {fromId} -.->|{ResolvedEdgeLabel(edge)}| {toId}");
        }

        sb.AppendLine("    classDef clsFlowEndpoint fill:#e0f2fe,stroke:#0284c7,stroke-width:2px,color:#0c4a6e");
        sb.AppendLine("    classDef clsFlowRequest fill:#dbeafe,stroke:#2563eb,stroke-width:1.5px,color:#1e3a8a");
        sb.AppendLine("    classDef clsFlowHandler fill:#f8fafc,stroke:#475569,stroke-width:1.5px,color:#1e293b");
        sb.AppendLine("    classDef clsFlowNotification fill:#fdf4ff,stroke:#a21caf,stroke-width:2px,color:#4a044e");
        sb.AppendLine("    classDef clsFlowDbContext fill:#ecfdf5,stroke:#059669,stroke-width:2px,color:#064e3b");
        if (subgraphLabel is not null)
            sb.AppendLine("    style flow fill:none,stroke:#94a3b8,stroke-dasharray:4 3");

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Endpoint labels always keep <c>Controller.Action</c> so the controller is
    /// identifiable even when the action name is unique within the section.
    /// </summary>
    private static string EndpointLabel(string name)
    {
        var lastDot = name.LastIndexOf('.');
        if (lastDot <= 0)
            return name;

        var previousDot = name.LastIndexOf('.', lastDot - 1);
        return name[(previousDot + 1)..];
    }

    private static string NodeLabel(string role, string name, IReadOnlyList<string> roleNames)
        => role == "endpoint" ? EndpointLabel(name) : ShortTypeName(name, roleNames);

    private static (string From, string To) Roles(FlowEdgeKind kind) => kind switch
    {
        FlowEdgeKind.EndpointToRequest => ("endpoint", "request"),
        FlowEdgeKind.RequestToHandler => ("request", "handler"),
        FlowEdgeKind.HandlerToRequest => ("handler", "request"),
        FlowEdgeKind.HandlerToNotification => ("handler", "notification"),
        FlowEdgeKind.HandlerToDbContext => ("handler", "dbcontext"),
        FlowEdgeKind.HandlerToDbContextResolved => ("handler", "dbcontext"),
        _ => ("request", "request"),
    };

    private static string Prefix(string role) => role switch
    {
        "endpoint" => "ep",
        "request" => "req",
        "handler" => "hdl",
        "notification" => "ntf",
        "dbcontext" => "dbc",
        _ => "n",
    };

    private static string ClassName(string role) => role switch
    {
        "endpoint" => "clsFlowEndpoint",
        "request" => "clsFlowRequest",
        "handler" => "clsFlowHandler",
        "notification" => "clsFlowNotification",
        "dbcontext" => "clsFlowDbContext",
        _ => "clsFlowRequest",
    };

    private static string EdgeLabel(FlowEdgeKind kind) => kind switch
    {
        FlowEdgeKind.EndpointToRequest => "dispatches",
        FlowEdgeKind.RequestToHandler => "handled by",
        FlowEdgeKind.HandlerToRequest => "sends",
        FlowEdgeKind.HandlerToNotification => "publishes",
        FlowEdgeKind.HandlerToDbContext => "uses",
        _ => "to",
    };

    /// <summary>Edge label for a DI-resolved hop: names the interface it resolves through.</summary>
    private static string ResolvedEdgeLabel(ResolvedEdge edge)
        => $"resolved via {ShortTypeName(edge.Via)}";
}
