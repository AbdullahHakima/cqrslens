namespace CqrsLens;

/// <summary>Groups requests into bounded CQRS diagram sections.</summary>
internal static class CqrsDiagramPlanner
{
    /// <summary>
    /// Builds one CQRS map for small solutions, or splits larger solutions by
    /// project and then into balanced parts. Every request appears exactly once.
    /// </summary>
    internal static IReadOnlyList<DiagramSection> Sections(AnalysisResult result)
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
            .GroupBy(CqrsRequestOrdering.ProjectKey, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new
            {
                Project = g.Key,
                Requests = CqrsRequestOrdering.OrderRequests(g),
            })
            .ToList();

        if (result.Requests.Count <= CqrsDiagramBuilder.MaxRequestsPerCqrsDiagram)
        {
            var requests = groups.SelectMany(g => g.Requests).ToList();
            var stats = CqrsStats.Create(requests, missingHandlers, missingValidators);
            var source = CqrsDiagramWriter.BuildSource(requests, missingHandlers, missingValidators, stats);
            var projectName = groups.Count == 1 && groups[0].Project != MermaidDiagramText.UnassignedProject
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
                    group.Project == MermaidDiagramText.UnassignedProject ? null : group.Project,
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
            var stats = CqrsStats.Create(chunk, missingHandlers, missingValidators);
            var source = CqrsDiagramWriter.BuildSource(chunk, missingHandlers, missingValidators, stats);
            if (chunk.Count <= 1 || DiagramBuilder.IsWithinMermaidLimits(source))
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

        var partCount = (requests.Count + CqrsDiagramBuilder.MaxRequestsPerCqrsDiagram - 1) /
                        CqrsDiagramBuilder.MaxRequestsPerCqrsDiagram;
        if (partCount <= 1)
            return new[] { requests.ToList() };

        var partSize = (requests.Count + partCount - 1) / partCount;
        var chunks = new List<List<CqrsRequest>>(partCount);
        for (var i = 0; i < requests.Count; i += partSize)
            chunks.Add(requests.Skip(i).Take(partSize).ToList());
        return chunks;
    }
}
