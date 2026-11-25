namespace CqrsLens;

/// <summary>
/// Discovers all system-wide entrypoints across HTTP Minimal APIs, MVC controllers,
/// FastEndpoints, Razor Pages, Blazor components, gRPC services, background workers, and
/// message/integration-event consumers, populating <see cref="AnalysisResult.SystemEntrypoints"/>
/// without altering <see cref="AnalysisResult.Endpoints"/>.
/// </summary>
internal static class SystemEntrypointDiscovery
{
    public static void Discover(
        IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles,
        AnalysisResult result,
        TypeHierarchy? hierarchy = null,
        SourcePathFilter? pathFilter = null)
        => Discover(SystemAnalysisContext.Create(parsedFiles, result, hierarchy, pathFilter));

    internal static void Discover(SystemAnalysisContext context)
    {
        var discovered = new List<SystemEntrypointInfo>();

        MinimalApiSystemScanner.Discover(context, discovered);
        ControllerAndFastEndpointSystemScanner.Discover(context, discovered);
        RazorAndBlazorSystemScanner.Discover(context, discovered);
        GrpcSystemScanner.Discover(context, discovered);
        WorkerAndConsumerSystemScanner.Discover(context, discovered);

        var deduped = new Dictionary<(string Project, SystemEntrypointKind Kind, string Method, string Route, string Name, string? File, int? Line), SystemEntrypointInfo>();
        foreach (var ep in discovered)
        {
            var key = (
                ep.ProjectName ?? string.Empty,
                ep.Kind,
                ep.Method,
                ep.RouteOrTrigger,
                ep.Name,
                ep.Location?.File,
                ep.Location?.Line);

            if (!deduped.TryGetValue(key, out var existing))
            {
                deduped[key] = ep;
                continue;
            }

            Merge(existing, ep);
        }

        context.Result.SystemEntrypoints.AddRange(deduped.Values
            .OrderBy(e => e.ProjectName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Kind)
            .ThenBy(e => e.Method, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.RouteOrTrigger, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Name, StringComparer.Ordinal)
            .ThenBy(e => e.Location?.File ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(e => e.Location?.Line ?? 0));
    }

    private static void Merge(SystemEntrypointInfo existing, SystemEntrypointInfo incoming)
    {
        foreach (var req in incoming.TargetRequests)
            SystemAnalysisContext.AddUniqueOrdinal(existing.TargetRequests, req);
        foreach (var svc in incoming.CalledServices)
            SystemAnalysisContext.AddUniqueOrdinal(existing.CalledServices, svc);
        foreach (var ctx in incoming.TouchedDbContexts)
            SystemAnalysisContext.AddUniqueOrdinal(existing.TouchedDbContexts, ctx);
        foreach (var ent in incoming.TouchedEntities)
            SystemAnalysisContext.AddUniqueOrdinal(existing.TouchedEntities, ent);
        foreach (var msg in incoming.PublishedMessages)
            SystemAnalysisContext.AddUniqueOrdinal(existing.PublishedMessages, msg);

        existing.TargetRequests.Sort(StringComparer.Ordinal);
        existing.CalledServices.Sort(StringComparer.Ordinal);
        existing.TouchedDbContexts.Sort(StringComparer.Ordinal);
        existing.TouchedEntities.Sort(StringComparer.Ordinal);
        existing.PublishedMessages.Sort(StringComparer.Ordinal);
    }
}
