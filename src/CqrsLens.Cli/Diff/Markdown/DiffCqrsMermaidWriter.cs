using System.Text;

using static CqrsLens.JsonReportGenerator;

namespace CqrsLens;

/// <summary>Builds the colored Mermaid delta map for added, modified and removed requests.</summary>
internal static class DiffCqrsMermaidWriter
{
    public static string? Build(RequestDiffSection requests)
    {
        var totalChanged = requests.Added.Count + requests.Modified.Count + requests.Removed.Count;
        if (totalChanged == 0 || totalChanged > DiagramBuilder.MaxRequestsPerCqrsDiagram)
            return null;

        var diagram = new StringBuilder();
        diagram.AppendLine("flowchart LR");
        diagram.AppendLine("    accTitle: CQRS diff map");
        diagram.AppendLine(
            $"    accDescr: {requests.Added.Count} added, {requests.Modified.Count} modified, " +
            $"{requests.Removed.Count} removed request(s).");

        var usedIds = new HashSet<string>(StringComparer.Ordinal);
        var idByKey = new Dictionary<string, string>(StringComparer.Ordinal);

        string Allocate(string key, string prefix, string name)
        {
            if (idByKey.TryGetValue(key, out var existing))
                return existing;

            var baseId = prefix + "_" + DiagramBuilder.MermaidId(name);
            var id = baseId;
            var suffix = 2;
            while (!usedIds.Add(id))
                id = $"{baseId}_{suffix++}";

            idByKey[key] = id;
            return id;
        }

        var items = new List<(JsonRequest Request, string StatusClass, string Prefix)>();
        foreach (var request in requests.Added)
            items.Add((request, "clsAdded", "+ "));
        foreach (var modified in requests.Modified)
            items.Add((modified.HeadSnapshot, "clsModified", "~ "));
        foreach (var request in requests.Removed)
            items.Add((request, "clsRemoved", "- "));

        foreach (var (request, statusClass, prefix) in items)
        {
            var requestId = Allocate("req:" + request.FullName + ":" + statusClass, "req", request.Name);
            var label = DiagramBuilder.MermaidLabel(prefix + request.Name);
            diagram.AppendLine($"    {requestId}[\"{label}\"]:::{statusClass}");

            foreach (var handler in request.Handlers
                         .Distinct(StringComparer.Ordinal)
                         .OrderBy(value => value, StringComparer.Ordinal))
            {
                var handlerName = CSharpScanner.SimpleName(handler);
                var handlerId = Allocate("hdl:" + handler, "hdl", handlerName);
                diagram.AppendLine($"    {handlerId}([\"{DiagramBuilder.MermaidLabel(handlerName)}\"]):::clsHandler");
                diagram.AppendLine($"    {requestId} --> {handlerId}");
            }

            foreach (var validator in request.Validators
                         .Distinct(StringComparer.Ordinal)
                         .OrderBy(value => value, StringComparer.Ordinal))
            {
                var validatorName = CSharpScanner.SimpleName(validator);
                var validatorId = Allocate("val:" + validator, "val", validatorName);
                diagram.AppendLine($"    {validatorId}[\"{DiagramBuilder.MermaidLabel(validatorName)}\"]:::clsValidator");
                diagram.AppendLine($"    {validatorId} -.->|validates| {requestId}");
            }
        }

        diagram.AppendLine("    classDef clsAdded fill:#ecfdf5,stroke:#059669,stroke-width:2px,color:#064e3b");
        diagram.AppendLine("    classDef clsModified fill:#eff6ff,stroke:#d97706,stroke-width:2px,color:#1e3a8a");
        diagram.AppendLine("    classDef clsRemoved fill:#fef2f2,stroke:#dc2626,stroke-width:2px,stroke-dasharray:4 3,color:#7f1d1d");
        diagram.AppendLine("    classDef clsHandler fill:#f8fafc,stroke:#475569,stroke-width:1.5px,color:#1e293b");
        diagram.AppendLine("    classDef clsValidator fill:#fff7ed,stroke:#c2410c,stroke-width:1.5px,color:#431407");

        return diagram.ToString().TrimEnd();
    }
}
