using System.Text;

namespace CqrsLens;

/// <summary>Writes a single bounded Mermaid CQRS map.</summary>
internal static class CqrsDiagramWriter
{
    internal static string BuildSource(
        IReadOnlyList<CqrsRequest> requests,
        HashSet<string> missingHandlers,
        HashSet<string> missingValidators,
        CqrsStats stats)
    {
        var ids = new MermaidIdAllocator();
        var groups = requests
            .GroupBy(CqrsRequestOrdering.ProjectKey, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new ProjectGroup(g.Key, CqrsRequestOrdering.OrderRequests(g)))
            .ToList();
        var orderedRequests = groups.SelectMany(g => g.Requests).ToList();

        var requestNameCounts = orderedRequests
            .GroupBy(r => r.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var projectIds = new Dictionary<string, string>(StringComparer.Ordinal);

        var description =
            $"{MermaidDiagramText.CountText(requests.Count, "request")}, " +
            $"{MermaidDiagramText.CountText(stats.Handlers, "handler")}, " +
            $"{MermaidDiagramText.CountText(stats.Validators, "validator")}";
        if (stats.MissingHandlers > 0)
            description += $", {stats.MissingHandlers} MissingHandler";
        if (stats.MissingValidators > 0)
            description += $", {stats.MissingValidators} CommandWithoutValidator";
        description += ".";

        var sb = new StringBuilder();
        sb.AppendLine("flowchart LR");
        sb.AppendLine("    accTitle: CQRS map");
        sb.AppendLine($"    accDescr: {description}");

        WriteProjectGroups(sb, groups, requestNameCounts, missingHandlers, missingValidators, ids, projectIds);

        var handlerFqns = orderedRequests
            .SelectMany(r => r.Handlers)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(h => h, StringComparer.Ordinal)
            .ToList();
        var handlerIds = WriteHandlers(sb, handlerFqns, ids);

        var validatorFqns = orderedRequests
            .SelectMany(r => r.Validators)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();
        var validatorIds = WriteValidators(sb, validatorFqns, ids);

        WriteRequestEdges(sb, orderedRequests, requestNameCounts, handlerIds, validatorIds, ids);

        foreach (var projectId in projectIds.Values)
            sb.AppendLine(
                $"    style {projectId} fill:none,stroke:#94a3b8,stroke-dasharray:4 3");
        EmitClassDefs(sb, stats);
        return sb.ToString().TrimEnd();
    }

    private static void WriteProjectGroups(
        StringBuilder sb,
        List<ProjectGroup> groups,
        Dictionary<string, int> requestNameCounts,
        HashSet<string> missingHandlers,
        HashSet<string> missingValidators,
        MermaidIdAllocator ids,
        Dictionary<string, string> projectIds)
    {
        foreach (var group in groups)
        {
            var projectId = ids.Allocate("project-group:" + group.Project, "prj", group.Project);
            projectIds[group.Project] = projectId;
            var subgraphLabel = group.Project == MermaidDiagramText.UnassignedProject
                ? "Unassigned requests"
                : $"Requests in {group.Project}";
            sb.AppendLine($"    subgraph {projectId}[\"{MermaidDiagramText.MermaidLabel(subgraphLabel)}\"]");
            foreach (var request in group.Requests)
            {
                var requestLabel = requestNameCounts[request.Name] > 1
                    ? request.FullName
                    : request.Name;
                var requestId = ids.Allocate(
                    "request:" + request.FullName,
                    "req",
                    requestLabel);
                sb.AppendLine(
                    $"        {requestId}[\"{MermaidDiagramText.MermaidLabel(requestLabel)}\"]" +
                    $":::{RequestClass(request, missingHandlers, missingValidators)}");
            }
            sb.AppendLine("    end");
        }
    }

    private static Dictionary<string, string> WriteHandlers(
        StringBuilder sb,
        List<string> handlerFqns,
        MermaidIdAllocator ids)
    {
        var handlerCounts = handlerFqns
            .GroupBy(CSharpScanner.SimpleName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var handlerIds = handlerFqns.ToDictionary(
            h => h,
            h => ids.Allocate(
                "handler:" + h,
                "hdl",
                handlerCounts[CSharpScanner.SimpleName(h)] > 1
                    ? h
                    : CSharpScanner.SimpleName(h)),
            StringComparer.Ordinal);
        foreach (var handler in handlerFqns)
        {
            var handlerName = CSharpScanner.SimpleName(handler);
            var handlerLabel = handlerCounts[handlerName] > 1 ? handler : handlerName;
            sb.AppendLine(
                $"    {handlerIds[handler]}([\"{MermaidDiagramText.MermaidLabel(handlerLabel)}\"])" +
                ":::clsHandler");
        }

        return handlerIds;
    }

    private static Dictionary<string, string> WriteValidators(
        StringBuilder sb,
        List<string> validatorFqns,
        MermaidIdAllocator ids)
    {
        var validatorCounts = validatorFqns
            .GroupBy(CSharpScanner.SimpleName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var validatorIds = validatorFqns.ToDictionary(
            v => v,
            v => ids.Allocate(
                "validator:" + v,
                "val",
                validatorCounts[CSharpScanner.SimpleName(v)] > 1
                    ? v
                    : CSharpScanner.SimpleName(v)),
            StringComparer.Ordinal);
        foreach (var validator in validatorFqns)
        {
            var validatorName = CSharpScanner.SimpleName(validator);
            var validatorLabel = validatorCounts[validatorName] > 1 ? validator : validatorName;
            sb.AppendLine(
                $"    {validatorIds[validator]}[\"{MermaidDiagramText.MermaidLabel(validatorLabel)}\"]" +
                ":::clsValidator");
        }

        return validatorIds;
    }

    private static void WriteRequestEdges(
        StringBuilder sb,
        List<CqrsRequest> orderedRequests,
        Dictionary<string, int> requestNameCounts,
        Dictionary<string, string> handlerIds,
        Dictionary<string, string> validatorIds,
        MermaidIdAllocator ids)
    {
        foreach (var request in orderedRequests)
        {
            var requestId = ids.Allocate(
                "request:" + request.FullName,
                "req",
                requestNameCounts[request.Name] > 1 ? request.FullName : request.Name);
            foreach (var handler in request.Handlers
                         .Distinct(StringComparer.Ordinal)
                         .OrderBy(h => h, StringComparer.Ordinal))
                sb.AppendLine($"    {requestId} --> {handlerIds[handler]}");
            foreach (var validator in request.Validators
                         .Distinct(StringComparer.Ordinal)
                         .OrderBy(v => v, StringComparer.Ordinal))
                sb.AppendLine($"    {validatorIds[validator]} -.->|validates| {requestId}");
        }
    }

    private static string RequestClass(
        CqrsRequest request,
        HashSet<string> missingHandlers,
        HashSet<string> missingValidators)
    {
        if (missingHandlers.Contains(request.FullName))
            return "clsMissingHandler";
        if (missingValidators.Contains(request.FullName))
            return "clsCommandNoValidator";

        return request.Kind switch
        {
            "Command" => "clsCommand",
            "Query" => "clsQuery",
            _ => "clsRequest",
        };
    }

    private static void EmitClassDefs(StringBuilder sb, CqrsStats stats)
    {
        sb.AppendLine("    classDef clsCommand fill:#e8f1ff,stroke:#2563eb,stroke-width:2px,color:#102a43");
        sb.AppendLine("    classDef clsQuery fill:#e6fffa,stroke:#0f766e,stroke-width:2px,color:#0f2e2e");
        sb.AppendLine("    classDef clsRequest fill:#f5f3ff,stroke:#6d28d9,stroke-width:2px,color:#2e1065");
        sb.AppendLine("    classDef clsHandler fill:#f8fafc,stroke:#475569,stroke-width:1.5px,color:#1e293b");
        sb.AppendLine("    classDef clsValidator fill:#fff7ed,stroke:#c2410c,stroke-width:1.5px,color:#431407");
        if (stats.MissingHandlers > 0)
            sb.AppendLine("    classDef clsMissingHandler fill:#fef2f2,stroke:#dc2626,stroke-width:2px,stroke-dasharray:4 3,color:#7f1d1d");
        if (stats.MissingValidators > 0)
            sb.AppendLine("    classDef clsCommandNoValidator fill:#e8f1ff,stroke:#d97706,stroke-width:2px,stroke-dasharray:4 3,color:#102a43");
    }

    private sealed record ProjectGroup(string Project, List<CqrsRequest> Requests);
}
