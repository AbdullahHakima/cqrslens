using System.Text;
using static CqrsLens.MermaidDiagramText;

namespace CqrsLens;

/// <summary>Builds bounded CQRS maps and their project/part sections.</summary>
internal static class CqrsDiagramBuilder
{
    internal const int MaxRequestsPerCqrsDiagram = 15;

    internal static string? CqrsMap(AnalysisResult result)
    {
        var sections = CqrsMapSections(result);
        return sections.Count > 0 ? sections[0].Source : null;
    }

    /// <summary>
    /// Builds one CQRS map for small solutions, or splits larger solutions by
    /// project and then into balanced parts. Every request appears exactly once.
    /// </summary>
    internal static IReadOnlyList<DiagramSection> CqrsMapSections(AnalysisResult result)
    {
        if (result.Requests.Count == 0)
            return Array.Empty<DiagramSection>();

        var missingHandlers = new HashSet<string>(StringComparer.Ordinal);
        var missingValidators = new HashSet<string>(StringComparer.Ordinal);
        foreach (var check in result.Checks)
        {
            if (check.Target is null) continue;
            if (check.Code == ArchitectureChecker.MissingHandlerCode)
                missingHandlers.Add(check.Target);
            else if (check.Code == ArchitectureChecker.CommandWithoutValidatorCode)
                missingValidators.Add(check.Target);
        }

        var groups = result.Requests
            .GroupBy(ProjectKey, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new
            {
                Project = g.Key,
                Requests = OrderRequests(g),
            })
            .ToList();

        if (result.Requests.Count <= MaxRequestsPerCqrsDiagram)
        {
            var requests = groups.SelectMany(g => g.Requests).ToList();
            var stats = GetStats(requests, missingHandlers, missingValidators);
            var source = BuildCqrsSource(requests, missingHandlers, missingValidators, stats);
            var projectName = groups.Count == 1 && groups[0].Project != UnassignedProject
                ? groups[0].Project
                : null;
            return new[]
            {
                new DiagramSection(
                    projectName,
                    requests,
                    source,
                    Part: 1,
                    PartCount: 1,
                    ProjectRequestCount: requests.Count,
                    stats.Handlers,
                    stats.Validators,
                    stats.MissingHandlers,
                    stats.MissingValidators),
            };
        }

        var sections = new List<DiagramSection>();
        foreach (var group in groups)
        {
            var built = new List<(List<CqrsRequest> Requests, string Source, CqrsStats Stats)>();
            foreach (var chunk in BalancedChunks(group.Requests))
                built.AddRange(BuildBounded(chunk, missingHandlers, missingValidators));

            for (var i = 0; i < built.Count; i++)
            {
                var part = built[i];
                sections.Add(new DiagramSection(
                    group.Project == UnassignedProject ? null : group.Project,
                    part.Requests,
                    part.Source,
                    Part: i + 1,
                    PartCount: built.Count,
                    ProjectRequestCount: group.Requests.Count,
                    part.Stats.Handlers,
                    part.Stats.Validators,
                    part.Stats.MissingHandlers,
                    part.Stats.MissingValidators));
            }
        }

        return sections;
    }

    private static string BuildCqrsSource(
        IReadOnlyList<CqrsRequest> requests,
        HashSet<string> missingHandlers,
        HashSet<string> missingValidators,
        CqrsStats stats)
    {
        var ids = new MermaidIdAllocator();
        var groups = requests
            .GroupBy(ProjectKey, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new
            {
                Project = g.Key,
                Requests = OrderRequests(g),
            })
            .ToList();
        var orderedRequests = groups.SelectMany(g => g.Requests).ToList();

        var requestNameCounts = orderedRequests
            .GroupBy(r => r.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var projectIds = new Dictionary<string, string>(StringComparer.Ordinal);

        var description =
            $"{CountText(requests.Count, "request")}, " +
            $"{CountText(stats.Handlers, "handler")}, " +
            $"{CountText(stats.Validators, "validator")}";
        if (stats.MissingHandlers > 0)
            description += $", {stats.MissingHandlers} MissingHandler";
        if (stats.MissingValidators > 0)
            description += $", {stats.MissingValidators} CommandWithoutValidator";
        description += ".";

        var sb = new StringBuilder();
        sb.AppendLine("flowchart LR");
        sb.AppendLine("    accTitle: CQRS map");
        sb.AppendLine($"    accDescr: {description}");

        foreach (var group in groups)
        {
            var projectId = ids.Allocate("project-group:" + group.Project, "prj", group.Project);
            projectIds[group.Project] = projectId;
            var subgraphLabel = group.Project == UnassignedProject
                ? "Unassigned requests"
                : $"Requests in {group.Project}";
            sb.AppendLine($"    subgraph {projectId}[\"{MermaidLabel(subgraphLabel)}\"]");
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
                    $"        {requestId}[\"{MermaidLabel(requestLabel)}\"]" +
                    $":::{RequestClass(request, missingHandlers, missingValidators)}");
            }
            sb.AppendLine("    end");
        }

        var handlerFqns = orderedRequests
            .SelectMany(r => r.Handlers)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(h => h, StringComparer.Ordinal)
            .ToList();
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
                $"    {handlerIds[handler]}([\"{MermaidLabel(handlerLabel)}\"])" +
                ":::clsHandler");
        }

        var validatorFqns = orderedRequests
            .SelectMany(r => r.Validators)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();
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
                $"    {validatorIds[validator]}[\"{MermaidLabel(validatorLabel)}\"]" +
                ":::clsValidator");
        }

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

        foreach (var projectId in projectIds.Values)
            sb.AppendLine(
                $"    style {projectId} fill:none,stroke:#94a3b8,stroke-dasharray:4 3");
        EmitCqrsClassDefs(sb, stats);
        return sb.ToString().TrimEnd();
    }

    private static List<(List<CqrsRequest> Requests, string Source, CqrsStats Stats)> BuildBounded(
        IReadOnlyList<CqrsRequest> requests,
        HashSet<string> missingHandlers,
        HashSet<string> missingValidators)
    {
        var result = new List<(List<CqrsRequest>, string, CqrsStats)>();
        var pending = new Stack<List<CqrsRequest>>();
        pending.Push(requests.ToList());

        while (pending.Count > 0)
        {
            var chunk = pending.Pop();
            var stats = GetStats(chunk, missingHandlers, missingValidators);
            var source = BuildCqrsSource(chunk, missingHandlers, missingValidators, stats);
            if (chunk.Count <= 1 || IsWithinMermaidLimits(source))
            {
                result.Add((chunk, source, stats));
                continue;
            }

            var middle = (chunk.Count + 1) / 2;
            pending.Push(chunk.Skip(middle).ToList());
            pending.Push(chunk.Take(middle).ToList());
        }

        return result;
    }

    private static IReadOnlyList<List<CqrsRequest>> BalancedChunks(IReadOnlyList<CqrsRequest> requests)
    {
        if (requests.Count == 0)
            return Array.Empty<List<CqrsRequest>>();

        var partCount = (requests.Count + MaxRequestsPerCqrsDiagram - 1) / MaxRequestsPerCqrsDiagram;
        if (partCount <= 1)
            return new[] { requests.ToList() };

        var partSize = (requests.Count + partCount - 1) / partCount;
        var chunks = new List<List<CqrsRequest>>(partCount);
        for (var i = 0; i < requests.Count; i += partSize)
            chunks.Add(requests.Skip(i).Take(partSize).ToList());
        return chunks;
    }

    private static List<CqrsRequest> OrderRequests(IEnumerable<CqrsRequest> requests)
        => requests
            .OrderBy(KindOrder)
            .ThenBy(r => r.FullName, StringComparer.Ordinal)
            .ToList();

    private static int KindOrder(CqrsRequest request)
        => request.Kind switch
        {
            "Command" => 0,
            "Query" => 1,
            _ => 2,
        };

    private static string ProjectKey(CqrsRequest request)
        => string.IsNullOrEmpty(request.ProjectName) ? UnassignedProject : request.ProjectName;

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

    private static CqrsStats GetStats(
        IReadOnlyList<CqrsRequest> requests,
        HashSet<string> missingHandlers,
        HashSet<string> missingValidators)
    {
        var requestNames = requests
            .Select(r => r.FullName)
            .ToHashSet(StringComparer.Ordinal);
        return new CqrsStats(
            requests.SelectMany(r => r.Handlers).Distinct(StringComparer.Ordinal).Count(),
            requests.SelectMany(r => r.Validators).Distinct(StringComparer.Ordinal).Count(),
            missingHandlers.Count(requestNames.Contains),
            missingValidators.Count(requestNames.Contains));
    }

    private static void EmitCqrsClassDefs(StringBuilder sb, CqrsStats stats)
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

    private readonly record struct CqrsStats(
        int Handlers,
        int Validators,
        int MissingHandlers,
        int MissingValidators);
}