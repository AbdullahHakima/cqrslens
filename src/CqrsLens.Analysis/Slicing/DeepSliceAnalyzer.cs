namespace CqrsLens;

/// <summary>
/// Enriches linked CQRS shapes with deep vertical-slice metadata: API entrypoints
/// (Controllers, Minimal APIs, Method-Group EndpointGroups, FastEndpoints), request payload
/// properties (including immutable getter-only properties), handler constructor dependencies,
/// touched EF Core DbSets/entities, and published notifications.
/// Runs after <see cref="Analyzer"/> shape discovery and linking so both text and Roslyn modes
/// produce identical deep slice traces.
/// </summary>
internal static class DeepSliceAnalyzer
{
    public static void Analyze(SourceWorkspace workspace, AnalysisResult result)
    {
        if (workspace.ParsedFiles.Count == 0 ||
            (result.Requests.Count == 0 && result.Notifications.Count == 0))
            return;

        var parsedFiles = workspace.SuccessfullyParsed
            .OrderBy(f => f.File.Path, StringComparer.OrdinalIgnoreCase)
            .Select(f => (f.File, f.Scan!, f.Mask!))
            .ToList();

        var phaseWatch = System.Diagnostics.Stopwatch.StartNew();
        var phaseAllocated = GC.GetTotalAllocatedBytes(precise: false);
        void Phase(string label)
        {
            phaseWatch.Stop();
            var allocated = GC.GetTotalAllocatedBytes(precise: false) - phaseAllocated;
            result.PhaseTimings.Add(new AnalysisPhaseTiming(
                $"Deep: {label}", phaseWatch.Elapsed.TotalMilliseconds, allocated));
            phaseWatch.Restart();
            phaseAllocated = GC.GetTotalAllocatedBytes(precise: false);
        }

        RequestPropertyPopulator.Populate(parsedFiles, result);
        Phase("request properties");
        HandlerDeepSliceAnalysis.Run(parsedFiles, result);
        Phase("handler slices");
        ApiEndpointDiscovery.Discover(parsedFiles, result);
        Phase("api entrypoints");
    }
}
