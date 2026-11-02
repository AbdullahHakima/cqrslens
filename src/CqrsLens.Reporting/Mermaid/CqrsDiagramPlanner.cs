namespace CqrsLens;

/// <summary>Groups requests into bounded CQRS diagram sections.</summary>
internal static class CqrsDiagramPlanner
{
    /// <summary>
    /// Builds one CQRS map for small solutions, or splits larger solutions by
    /// project and then into balanced parts. Every request appears exactly once.
    /// </summary>
    internal static IReadOnlyList<DiagramSection> Sections(AnalysisResult result)
        => Sections(result, CqrsMapSplitMode.Count);

    /// <summary>
    /// Builds CQRS map sections. <see cref="CqrsMapSplitMode.Count"/> keeps the
    /// default project + fixed-size split; <see cref="CqrsMapSplitMode.Feature"/>
    /// groups requests by the feature segment of their namespace so each feature
    /// gets its own diagram; <see cref="CqrsMapSplitMode.Unsplit"/> emits one
    /// diagram per project and only splits a project further when its map exceeds
    /// the renderer's safety limit. In every mode each request appears exactly once.
    /// </summary>
    internal static IReadOnlyList<DiagramSection> Sections(AnalysisResult result, CqrsMapSplitMode split)
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
            .GroupBy(split == CqrsMapSplitMode.Feature
                ? CqrsRequestOrdering.FeatureKey
                : CqrsRequestOrdering.ProjectKey, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new
            {
                Label = split == CqrsMapSplitMode.Feature
                    ? CqrsRequestOrdering.FeatureHeading(g.First())
                    : g.Key,
                Key = g.Key,
                Requests = CqrsRequestOrdering.OrderRequests(g),
            })
            .ToList();

        if (split == CqrsMapSplitMode.Count &&
            result.Requests.Count <= CqrsDiagramBuilder.MaxRequestsPerCqrsDiagram)
        {
            var requests = groups.SelectMany(g => g.Requests).ToList();
            var stats = CqrsStats.Create(requests, missingHandlers, missingValidators);
            var source = CqrsDiagramWriter.BuildSource(requests, missingHandlers, missingValidators, stats);
            var projectName = groups.Count == 1 && groups[0].Key != MermaidDiagramText.UnassignedProject
                ? groups[0].Label
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
            // Unsplit: one candidate chunk per project; BuildBounded still splits it
            // only when the map would exceed the renderer's safety limit.
            var chunks = split == CqrsMapSplitMode.Unsplit
                ? (IReadOnlyList<List<CqrsRequest>>)new[] { group.Requests.ToList() }
                : FeatureChunks(group.Requests);
            foreach (var chunk in chunks)
                built.AddRange(BuildBounded(chunk, missingHandlers, missingValidators));

            for (var i = 0; i < built.Count; i++)
            {
                var part = built[i];
                sections.Add(new DiagramSection(
                    group.Key == MermaidDiagramText.UnassignedProject ? null : group.Label,
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

    /// <summary>
    /// Splits feature-ordered requests into bounded chunks, keeping whole features
    /// together so a split point never cuts a feature in half (a feature larger than
    /// the bound is the only case that is itself split).
    /// </summary>
    private static IReadOnlyList<List<CqrsRequest>> FeatureChunks(IReadOnlyList<CqrsRequest> requests)
    {
        if (requests.Count == 0)
            return Array.Empty<List<CqrsRequest>>();

        var max = CqrsDiagramBuilder.MaxRequestsPerCqrsDiagram;
        var partCount = (requests.Count + max - 1) / max;
        var target = (requests.Count + partCount - 1) / partCount;

        var runs = new List<List<CqrsRequest>>();
        foreach (var request in requests)
        {
            if (runs.Count == 0 ||
                !string.Equals(
                    CqrsRequestOrdering.FeatureName(runs[^1][0]),
                    CqrsRequestOrdering.FeatureName(request),
                    StringComparison.Ordinal))
            {
                runs.Add(new List<CqrsRequest>());
            }
            runs[^1].Add(request);
        }

        var chunks = new List<List<CqrsRequest>>();
        var current = new List<CqrsRequest>();
        void Flush()
        {
            if (current.Count == 0)
                return;
            chunks.Add(current);
            current = new List<CqrsRequest>();
        }

        foreach (var run in runs)
        {
            if (run.Count > max)
            {
                // A single feature too large to fit: split it evenly.
                Flush();
                for (var i = 0; i < run.Count; i += target)
                    chunks.Add(run.Skip(i).Take(target).ToList());
                continue;
            }

            if (current.Count > 0 && current.Count + run.Count > target)
                Flush();
            current.AddRange(run);
        }

        Flush();
        return chunks;
    }
}
