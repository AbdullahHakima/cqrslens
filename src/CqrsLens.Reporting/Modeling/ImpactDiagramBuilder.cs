using System.Text;
using static CqrsLens.MermaidDiagramText;

namespace CqrsLens;

/// <summary>
/// Builds a bounded 3-column Mermaid <c>flowchart LR</c> visualizing upstream and downstream
/// blast radius around a <see cref="ChangeImpactReport"/> target while highlighting uncovered hotspots.
/// </summary>
public static class ImpactDiagramBuilder
{
    private const int MaxDiagramNodes = 24;

    public static string? BuildMermaid(ChangeImpactReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (report.Target is null)
            return null;

        var uncovered = new HashSet<string>(report.UncoveredHotspots, StringComparer.OrdinalIgnoreCase);
        var coveredTargets = new HashSet<string>(
            report.ExistingTestReferences.Select(t => t.TargetFullName),
            StringComparer.Ordinal);

        var ids = new MermaidIdAllocator();
        var centerKey = $"{report.Target.Kind}|{report.Target.FullName ?? report.Target.Name}";
        var centerName = report.Target.Kind is "endpoint" or "entrypoint" or "datastore"
            ? (!string.IsNullOrWhiteSpace(report.Target.Name) ? report.Target.Name : report.Target.FullName ?? report.Target.Name)
            : ShortTypeName(report.Target.FullName ?? report.Target.Name);
        var centerLabel = $"{report.Target.Kind}: {centerName}";
        var centerId = ids.Allocate(centerKey, "tgt", centerLabel);

        var upstreamNodes = new List<(string Id, string Label, string Cls)>();
        var downstreamNodes = new List<(string Id, string Label, string Cls)>();
        var seenDisplayKey = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var edges = new List<(string FromId, string ToId, string Label, bool Dotted)>();

        // Prioritize hotspots, then depth-1 balanced across kinds, then deeper impacts.
        var prioritized = SelectBalancedHighSignalItems(report.ImpactedItems, uncovered);

        foreach (var item in prioritized)
        {
            var shortName = item.Kind is "endpoint" or "entrypoint" or "datastore"
                ? (!string.IsNullOrWhiteSpace(item.Name) ? item.Name : item.FullName)
                : ShortTypeName(item.FullName);
            var isUpstream = string.Equals(item.Direction, "upstream", StringComparison.Ordinal);
            var dedupKey = $"{(isUpstream ? "up" : "down")}|{item.Kind}|{shortName}";
            if (!seenDisplayKey.Add(dedupKey))
                continue;

            var isHotspot = uncovered.Contains(item.FullName) || uncovered.Contains(item.Name);
            var isTested = coveredTargets.Contains(item.FullName) || coveredTargets.Contains(item.Name);
            var cls = isHotspot
                ? "clsImpHotspot"
                : isTested
                    ? "clsImpTested"
                    : isUpstream
                        ? "clsImpUpstream"
                        : "clsImpDownstream";
            var prefix = item.Kind switch
            {
                "request" => "req",
                "handler" => "hdl",
                "project" => "prj",
                "endpoint" or "entrypoint" => "ep",
                "datastore" => "ds",
                "event" => "evt",
                _ => "imp",
            };
            var label = isHotspot ? $"{item.Kind}: {shortName} (! untested)" : $"{item.Kind}: {shortName}";
            var id = ids.Allocate($"{dedupKey}|{item.FullName}", prefix, label);

            if (isUpstream)
            {
                upstreamNodes.Add((id, label, cls));
                edges.Add((id, centerId, $"d={item.Depth}", item.Depth > 1));
            }
            else
            {
                downstreamNodes.Add((id, label, cls));
                edges.Add((centerId, id, $"d={item.Depth}", item.Depth > 1));
            }

            if (upstreamNodes.Count + downstreamNodes.Count >= MaxDiagramNodes)
                break;
        }

        // Also surface any uncovered hotspot not yet represented if budget permits.
        foreach (var hotspot in report.UncoveredHotspots.Take(6))
        {
            if (upstreamNodes.Count + downstreamNodes.Count >= MaxDiagramNodes)
                break;

            var shortHotspot = ShortTypeName(hotspot);
            if (upstreamNodes.Any(v => v.Label.Contains(shortHotspot, StringComparison.Ordinal)) ||
                downstreamNodes.Any(v => v.Label.Contains(shortHotspot, StringComparison.Ordinal)))
            {
                continue;
            }

            var label = $"hotspot: {shortHotspot} (! untested)";
            var id = ids.Allocate($"hotspot|{hotspot}", "hot", label);
            upstreamNodes.Add((id, label, "clsImpHotspot"));
            edges.Add((id, centerId, "untested", true));
        }

        var shownCount = upstreamNodes.Count + downstreamNodes.Count;
        var summaryNote = report.ImpactedItems.Count > shownCount
            ? $"{report.ImpactedItems.Count} impacted, {report.UncoveredHotspots.Count} uncovered hotspot(s), top {shownCount} shown"
            : $"{report.ImpactedItems.Count} impacted, {report.UncoveredHotspots.Count} uncovered hotspot(s)";

        var sb = new StringBuilder();
        sb.AppendLine("flowchart LR");
        sb.AppendLine("    accTitle: Change Impact Blast Radius");
        sb.AppendLine($"    accDescr: Blast radius for {MermaidLabel(centerLabel)} ({summaryNote}).");

        if (upstreamNodes.Count > 0)
        {
            sb.AppendLine("    subgraph grp_upstream[\"Upstream Impact\"]");
            foreach (var spec in upstreamNodes)
                sb.AppendLine($"        {spec.Id}[\"{MermaidLabel(spec.Label)}\"]:::{spec.Cls}");
            sb.AppendLine("    end");
        }

        sb.AppendLine("    subgraph grp_target[\"Target\"]");
        sb.AppendLine($"        {centerId}([\"{MermaidLabel(centerLabel)}\"]):::clsImpTarget");
        sb.AppendLine("    end");

        if (downstreamNodes.Count > 0)
        {
            sb.AppendLine("    subgraph grp_downstream[\"Downstream Impact\"]");
            foreach (var spec in downstreamNodes)
                sb.AppendLine($"        {spec.Id}[\"{MermaidLabel(spec.Label)}\"]:::{spec.Cls}");
            sb.AppendLine("    end");
        }

        foreach (var (fromId, toId, label, dotted) in edges.Distinct())
        {
            var arrow = dotted ? "-.->" : "-->";
            sb.AppendLine($"    {fromId} {arrow}|{MermaidLabel(label)}| {toId}");
        }

        sb.AppendLine("    classDef clsImpTarget fill:#dbeafe,stroke:#2563eb,stroke-width:2px,color:#1e3a8a");
        sb.AppendLine("    classDef clsImpUpstream fill:#f8fafc,stroke:#475569,stroke-width:1.5px,color:#1e293b");
        sb.AppendLine("    classDef clsImpDownstream fill:#ecfdf5,stroke:#059669,stroke-width:1.5px,color:#064e3b");
        sb.AppendLine("    classDef clsImpTested fill:#dcfce7,stroke:#16a34a,stroke-width:2px,color:#14532d");
        sb.AppendLine("    classDef clsImpHotspot fill:#fee2e2,stroke:#dc2626,stroke-width:2px,color:#7f1d1d");
        return sb.ToString().TrimEnd();
    }

    private static List<ChangeImpactItem> SelectBalancedHighSignalItems(
        IReadOnlyList<ChangeImpactItem> items,
        HashSet<string> uncovered)
    {
        if (items.Count <= MaxDiagramNodes)
            return items.ToList();

        var ordered = new List<ChangeImpactItem>(items.Count);

        // 1. Uncovered hotspots first.
        var remaining = new List<ChangeImpactItem>();
        foreach (var item in items)
        {
            if (uncovered.Contains(item.FullName) || uncovered.Contains(item.Name))
                ordered.Add(item);
            else
                remaining.Add(item);
        }

        // 2. Round-robin across (Direction, Kind) at shallowest depths so datastores, projects, handlers, etc. all appear.
        var buckets = remaining
            .OrderBy(i => i.Depth)
            .GroupBy(i => $"{i.Direction}|{i.Kind}", StringComparer.OrdinalIgnoreCase)
            .Select(g => new Queue<ChangeImpactItem>(g))
            .ToList();

        while (ordered.Count < items.Count)
        {
            var progressed = false;
            foreach (var bucket in buckets)
            {
                if (bucket.Count > 0)
                {
                    ordered.Add(bucket.Dequeue());
                    progressed = true;
                }
            }
            if (!progressed)
                break;
        }

        return ordered;
    }
}
