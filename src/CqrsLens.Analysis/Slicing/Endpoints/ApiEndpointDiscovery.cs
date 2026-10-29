namespace CqrsLens;

/// <summary>
/// Discovers HTTP entrypoints and links them to the CQRS request they dispatch:
/// MVC controllers, minimal APIs, EndpointGroup method groups and FastEndpoints.
/// </summary>
internal static class ApiEndpointDiscovery
{
    internal static void Discover(
        List<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles,
        AnalysisResult result)
    {
        var requestsBySimple = result.Requests
            .GroupBy(r => r.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        // Build constant string lookup for FastEndpoints route constants
        // (e.g. CreateContributorRequest.Route).
        var constStrings = EndpointRequestMatcher.BuildConstStringLookup(parsedFiles);

        foreach (var (file, scan, mask) in parsedFiles)
        {
            ControllerEndpointDiscovery.Discover(file, scan, mask, requestsBySimple, result);
            MinimalApiEndpointDiscovery.Discover(file, scan, mask, requestsBySimple, result);
            FastEndpointDiscovery.Discover(file, scan, mask, requestsBySimple, constStrings, result);
        }

        result.Endpoints.Sort((a, b) =>
        {
            var byRoute = string.Compare(a.Route, b.Route, StringComparison.OrdinalIgnoreCase);
            if (byRoute != 0) return byRoute;
            var byMethod = string.Compare(a.HttpMethod, b.HttpMethod, StringComparison.OrdinalIgnoreCase);
            return byMethod != 0 ? byMethod : string.Compare(a.Name, b.Name, StringComparison.Ordinal);
        });
    }
}
