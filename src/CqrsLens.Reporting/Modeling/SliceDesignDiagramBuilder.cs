using System.Text;
using static CqrsLens.MermaidDiagramText;

namespace CqrsLens;

/// <summary>
/// Builds a Mermaid <c>flowchart LR</c> comparing designed vs. observed stages of a
/// <see cref="SliceDesignEvaluation"/> vertical slice, color-coded by verification status.
/// </summary>
public static class SliceDesignDiagramBuilder
{
    public static string BuildMermaid(SliceDesignEvaluation evaluation)
    {
        ArgumentNullException.ThrowIfNull(evaluation);

        var design = evaluation.Design;
        var checksByField = evaluation.Checks
            .GroupBy(c => c.Field, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        string StatusOf(params string[] fields)
        {
            var statuses = fields
                .Where(f => checksByField.ContainsKey(f))
                .Select(f => checksByField[f].Status)
                .ToList();
            if (statuses.Count == 0) return "neutral";
            if (statuses.Contains(SliceDesignEvaluator.Mismatch, StringComparer.Ordinal))
                return SliceDesignEvaluator.Mismatch;
            if (statuses.Contains(SliceDesignEvaluator.NotDeterminable, StringComparer.Ordinal))
                return SliceDesignEvaluator.NotDeterminable;
            if (statuses.Contains(SliceDesignEvaluator.Missing, StringComparer.Ordinal))
                return SliceDesignEvaluator.Missing;
            return SliceDesignEvaluator.Matched;
        }

        string ClassFor(string status) => status switch
        {
            SliceDesignEvaluator.Matched => "clsSliceMatched",
            SliceDesignEvaluator.Missing => "clsSliceMissing",
            SliceDesignEvaluator.Mismatch => "clsSliceMismatch",
            SliceDesignEvaluator.NotDeterminable => "clsSliceUnknown",
            _ => "clsSliceNeutral",
        };

        var ids = new MermaidIdAllocator();
        var sb = new StringBuilder();
        sb.AppendLine("flowchart LR");
        sb.AppendLine("    accTitle: Slice Design Blueprint");
        sb.AppendLine($"    accDescr: Slice design blueprint for {MermaidLabel(design.Request)} ({MermaidLabel(evaluation.Status)}).");

        var reqStatus = StatusOf("request", "project", "properties");
        var reqLabel = design.IsCqrsKind || design.ResponseType is not null
            ? $"{design.Kind}: {ShortTypeName(design.Request)} ({design.ResponseType ?? "void"})"
            : $"{design.Kind}: {ShortTypeName(design.Request)}";
        var reqId = ids.Allocate("req", "req", reqLabel);
        sb.AppendLine($"    {reqId}[\"{MermaidLabel(reqLabel)}\"]:::{ClassFor(reqStatus)}");

        if (design.Endpoint is not null || checksByField.ContainsKey("endpoint"))
        {
            var ep = design.Endpoint;
            var epStatus = StatusOf("endpoint");
            var epLabel = ep is null ? "Endpoint" : $"{ep.Method.ToUpperInvariant()} {ep.Route}";
            var epId = ids.Allocate("ep", "ep", epLabel);
            sb.AppendLine($"    {epId}([\"{MermaidLabel(epLabel)}\"]):::{ClassFor(epStatus)}");
            var arrow = epStatus == SliceDesignEvaluator.Missing ? "-.->" : "-->";
            sb.AppendLine($"    {epId} {arrow}|{MermaidLabel(epStatus)}| {reqId}");
        }

        if (!string.IsNullOrWhiteSpace(design.Validator) || checksByField.ContainsKey("validator"))
        {
            var valStatus = StatusOf("validator");
            var valLabel = !string.IsNullOrWhiteSpace(design.Validator)
                ? $"Validator: {ShortTypeName(design.Validator)}"
                : "Validator: none expected";
            var valId = ids.Allocate("val", "val", valLabel);
            sb.AppendLine($"    {valId}[\"{MermaidLabel(valLabel)}\"]:::{ClassFor(valStatus)}");
            var arrow = valStatus == SliceDesignEvaluator.Missing ? "-.->" : "-->";
            sb.AppendLine($"    {reqId} {arrow}|{MermaidLabel(valStatus)}| {valId}");
        }

        string? hdlId = null;
        if (design.IsCqrsKind || !string.IsNullOrWhiteSpace(design.Handler) || checksByField.ContainsKey("handler"))
        {
            var hdlStatus = StatusOf("handler");
            var hdlLabel = !string.IsNullOrWhiteSpace(design.Handler)
                ? $"Handler: {ShortTypeName(design.Handler)}"
                : "Handler";
            hdlId = ids.Allocate("hdl", "hdl", hdlLabel);
            sb.AppendLine($"    {hdlId}[\"{MermaidLabel(hdlLabel)}\"]:::{ClassFor(hdlStatus)}");
            var hdlArrow = hdlStatus == SliceDesignEvaluator.Missing ? "-.->" : "-->";
            sb.AppendLine($"    {reqId} {hdlArrow}|{MermaidLabel(hdlStatus)}| {hdlId}");
        }

        var pivotId = hdlId ?? reqId;
        var downstreamId = pivotId;

        if (!design.IsCqrsKind)
        {
            if (design.Dependencies is { Count: > 0 } || checksByField.ContainsKey("dependencies"))
            {
                var depStatus = StatusOf("dependencies");
                var depLabel = design.Dependencies is { Count: > 0 }
                    ? $"Dependencies: {string.Join(", ", design.Dependencies.Select(d => ShortTypeName(d)))}"
                    : "Dependencies: none expected";
                var depId = ids.Allocate("dep", "svc", depLabel);
                sb.AppendLine($"    {depId}[\"{MermaidLabel(depLabel)}\"]:::{ClassFor(depStatus)}");
                var arrow = depStatus == SliceDesignEvaluator.Missing ? "-.->" : "-->";
                sb.AppendLine($"    {pivotId} {arrow}|{MermaidLabel(depStatus)}| {depId}");

                if (design.Dependencies is { Count: > 0 } && depStatus != SliceDesignEvaluator.Missing)
                    downstreamId = depId;
            }

            if (design.ForbiddenDependencies is { Count: > 0 } || checksByField.ContainsKey("forbiddenDependencies"))
            {
                var forbStatus = StatusOf("forbiddenDependencies");
                var forbLabel = design.ForbiddenDependencies is { Count: > 0 }
                    ? $"Forbids: {string.Join(", ", design.ForbiddenDependencies.Select(d => ShortTypeName(d)))}"
                    : "Forbidden dependencies";
                var forbId = ids.Allocate("forb", "svc", forbLabel);
                sb.AppendLine($"    {forbId}[\"{MermaidLabel(forbLabel)}\"]:::{ClassFor(forbStatus)}");
                var arrow = forbStatus == SliceDesignEvaluator.Missing ? "-.->" : "-->";
                sb.AppendLine($"    {pivotId} {arrow}|{MermaidLabel(forbStatus)}| {forbId}");
            }
        }

        if (design.Persistence is not null ||
            checksByField.ContainsKey("persistence.dbContexts") ||
            checksByField.ContainsKey("persistence.entities"))
        {
            var dbStatus = StatusOf("persistence.dbContexts", "persistence.entities");
            var ctxPart = design.Persistence?.DbContexts is { Count: > 0 }
                ? string.Join(", ", design.Persistence.DbContexts.Select(c => ShortTypeName(c)))
                : "";
            var entPart = design.Persistence?.Entities is { Count: > 0 }
                ? string.Join(", ", design.Persistence.Entities)
                : "";
            var labelParts = new List<string>();
            if (ctxPart.Length > 0) labelParts.Add(ctxPart);
            if (entPart.Length > 0) labelParts.Add(entPart);
            var dbLabel = labelParts.Count > 0 ? $"Data: {string.Join(" / ", labelParts)}" : "Persistence";
            var dbId = ids.Allocate("db", "dbc", dbLabel);
            sb.AppendLine($"    {dbId}[\"{MermaidLabel(dbLabel)}\"]:::{ClassFor(dbStatus)}");
            var dbArrow = dbStatus == SliceDesignEvaluator.Missing ? "-.->" : "-->";
            sb.AppendLine($"    {downstreamId} {dbArrow}|{MermaidLabel(dbStatus)}| {dbId}");
        }

        if (design.Publishes is { Count: > 0 } || checksByField.ContainsKey("publishes"))
        {
            var pubStatus = StatusOf("publishes");
            var pubLabel = design.Publishes is { Count: > 0 }
                ? $"Publishes: {string.Join(", ", design.Publishes.Select(p => ShortTypeName(p)))}"
                : "Publishes: none expected";
            var pubId = ids.Allocate("pub", "ntf", pubLabel);
            sb.AppendLine($"    {pubId}[\"{MermaidLabel(pubLabel)}\"]:::{ClassFor(pubStatus)}");
            var arrow = pubStatus == SliceDesignEvaluator.Missing ? "-.->" : "-->";
            sb.AppendLine($"    {downstreamId} {arrow}|{MermaidLabel(pubStatus)}| {pubId}");
        }

        if (design.Dispatches is { Count: > 0 } || checksByField.ContainsKey("dispatches"))
        {
            var dispStatus = StatusOf("dispatches");
            var dispLabel = design.Dispatches is { Count: > 0 }
                ? $"Dispatches: {string.Join(", ", design.Dispatches.Select(d => ShortTypeName(d)))}"
                : "Dispatches: none expected";
            var dispId = ids.Allocate("disp", "req", dispLabel);
            sb.AppendLine($"    {dispId}[\"{MermaidLabel(dispLabel)}\"]:::{ClassFor(dispStatus)}");
            var arrow = dispStatus == SliceDesignEvaluator.Missing ? "-.->" : "-->";
            sb.AppendLine($"    {downstreamId} {arrow}|{MermaidLabel(dispStatus)}| {dispId}");
        }

        if (design.IsCqrsKind &&
            (design.Dependencies is { Count: > 0 } ||
             design.ForbiddenDependencies is { Count: > 0 } ||
             checksByField.ContainsKey("dependencies") ||
             checksByField.ContainsKey("forbiddenDependencies")))
        {
            var depStatus = StatusOf("dependencies", "forbiddenDependencies");
            var depParts = new List<string>();
            if (design.Dependencies is { Count: > 0 })
                depParts.Add($"needs: {string.Join(", ", design.Dependencies.Select(d => ShortTypeName(d)))}");
            if (design.ForbiddenDependencies is { Count: > 0 })
                depParts.Add($"forbids: {string.Join(", ", design.ForbiddenDependencies.Select(d => ShortTypeName(d)))}");
            var depLabel = depParts.Count > 0 ? string.Join(" ; ", depParts) : "Dependencies";
            var depId = ids.Allocate("dep", "svc", depLabel);
            sb.AppendLine($"    {depId}[\"{MermaidLabel(depLabel)}\"]:::{ClassFor(depStatus)}");
            var arrow = depStatus == SliceDesignEvaluator.Missing ? "-.->" : "-->";
            sb.AppendLine($"    {pivotId} {arrow}|{MermaidLabel(depStatus)}| {depId}");
        }

        sb.AppendLine("    classDef clsSliceMatched fill:#dcfce7,stroke:#16a34a,stroke-width:2px,color:#14532d");
        sb.AppendLine("    classDef clsSliceMissing fill:#fef3c7,stroke:#d97706,stroke-width:2px,color:#78350f");
        sb.AppendLine("    classDef clsSliceMismatch fill:#fee2e2,stroke:#dc2626,stroke-width:2px,color:#7f1d1d");
        sb.AppendLine("    classDef clsSliceUnknown fill:#f3e8ff,stroke:#9333ea,stroke-width:2px,color:#581c87");
        sb.AppendLine("    classDef clsSliceNeutral fill:#f8fafc,stroke:#64748b,stroke-width:1.5px,color:#1e293b");
        return sb.ToString().TrimEnd();
    }
}
