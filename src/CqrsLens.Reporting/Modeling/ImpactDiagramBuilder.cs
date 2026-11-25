using System.Text;
using static CqrsLens.MermaidDiagramText;

namespace CqrsLens;

/// <summary>
/// Builds a Mermaid <c>flowchart LR</c> visualizing bidirectional dependency and flow
/// blast radius around a <see cref="ChangeImpactReport"/> target, highlighting
/// uncovered hotspots.
/// </summary>
public static class ImpactDiagramBuilder
{
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
        var centerName = report.Target.Kind is "endpoint" or "entrypoint"
            ? (!string.IsNullOrWhiteSpace(report.Target.Name) ? report.Target.Name : report.Target.FullName ?? report.Target.Name)
            : ShortTypeName(report.Target.FullName ?? report.Target.Name);
        var centerLabel = $"{report.Target.Kind}: {centerName}";
        var centerId = ids.Allocate(centerKey, "tgt", centerLabel);

        var nodeSpecs = new Dictionary<string, (string Id, string Label, string Cls)>(StringComparer.Ordinal);
        var edges = new List<(string FromId, string ToId, string Label, bool Dotted)>();

        foreach (var item in report.ImpactedItems.Take(60))
        {
            var key = $"{item.Kind}|{item.FullName}";
            if (!nodeSpecs.TryGetValue(key, out var spec))
            {
                var isHotspot = uncovered.Contains(item.FullName) || uncovered.Contains(item.Name);
                var isTested = coveredTargets.Contains(item.FullName) || coveredTargets.Contains(item.Name);
                var cls = isHotspot
                    ? "clsImpHotspot"
                    : isTested
                        ? "clsImpTested"
                        : item.Direction == "upstream"
                            ? "clsImpUpstream"
                            : "clsImpDownstream";
                var shortName = item.Kind is "endpoint" or "entrypoint"
                    ? (!string.IsNullOrWhiteSpace(item.Name) ? item.Name : item.FullName)
                    : ShortTypeName(item.FullName);
                var prefix = item.Kind switch
                {
                    "request" => "req",
                    "handler" => "hdl",
                    "project" => "prj",
                    "endpoint" or "entrypoint" => "ep",
                    "event" => "evt",
                    _ => "imp",
                };
                var label = isHotspot ? $"{item.Kind}: {shortName} (! untested)" : $"{item.Kind}: {shortName}";
                var id = ids.Allocate(key, prefix, label);
                spec = (id, label, cls);
                nodeSpecs[key] = spec;
            }

            if (string.Equals(item.Direction, "upstream", StringComparison.Ordinal))
                edges.Add((spec.Id, centerId, $"d={item.Depth} upstream", item.Depth > 1));
            else
                edges.Add((centerId, spec.Id, $"d={item.Depth} downstream", item.Depth > 1));
        }

        // Also surface any uncovered hotspot not already represented in first 60 items.
        foreach (var hotspot in report.UncoveredHotspots.Take(10))
        {
            if (nodeSpecs.Values.Any(v => v.Label.Contains(ShortTypeName(hotspot), StringComparison.Ordinal)))
                continue;
            var key = $"hotspot|{hotspot}";
            var label = $"hotspot: {ShortTypeName(hotspot)} (! untested)";
            var id = ids.Allocate(key, "hot", label);
            nodeSpecs[key] = (id, label, "clsImpHotspot");
            edges.Add((id, centerId, "untested", true));
        }

        var sb = new StringBuilder();
        sb.AppendLine("flowchart LR");
        sb.AppendLine("    accTitle: Change Impact Blast Radius");
        sb.AppendLine($"    accDescr: Blast radius for {MermaidLabel(centerLabel)} ({report.ImpactedItems.Count} impacted, {report.UncoveredHotspots.Count} uncovered hotspot(s)).");
        sb.AppendLine($"    {centerId}([\"{MermaidLabel(centerLabel)}\"]):::clsImpTarget");

        foreach (var spec in nodeSpecs.Values)
            sb.AppendLine($"    {spec.Id}[\"{MermaidLabel(spec.Label)}\"]:::{spec.Cls}");

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
}
