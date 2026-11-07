namespace CqrsLens;

/// <summary>
/// Builds the endpoint catalog: exactly one row per discovered endpoint, in a
/// deterministic order (project, then route, then name). The dispatched handler is
/// only shown when the endpoint's target request resolves to a discovered request
/// in the model; unresolved targets are explicit, never guessed. Presentation only.
/// </summary>
internal static class EndpointCatalogView
{
    /// <summary>One endpoint row. <see cref="Target"/> is "unresolved" when no request is known.</summary>
    internal sealed record Row(
        string Method,
        string Route,
        string Endpoint,
        string Project,
        string Target,
        string Handler,
        bool Inferred);

    internal sealed record Summary(
        IReadOnlyList<Row> Rows,
        int EndpointCount,
        int InferredCount,
        int ProjectCount);

    internal static Summary Build(AnalysisResult result)
    {
        var requestsByFullName = result.Requests
            .GroupBy(r => r.FullName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var requestsByName = result.Requests
            .GroupBy(r => r.Name, StringComparer.Ordinal)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var rows = result.Endpoints
            .OrderBy(e => e.ProjectName ?? MermaidDiagramText.UnassignedProject, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Route, StringComparer.Ordinal)
            .ThenBy(e => e.Name, StringComparer.Ordinal)
            .Select(e => BuildRow(e, requestsByFullName, requestsByName))
            .ToList();

        return new Summary(
            rows,
            rows.Count,
            rows.Count(r => r.Inferred),
            rows.Select(r => r.Project).Distinct(StringComparer.Ordinal).Count());
    }

    private static Row BuildRow(
        EndpointInfo endpoint,
        IReadOnlyDictionary<string, CqrsRequest> byFullName,
        IReadOnlyDictionary<string, CqrsRequest> byName)
    {
        CqrsRequest? request = null;
        if (!string.IsNullOrEmpty(endpoint.TargetRequestFullName))
            byFullName.TryGetValue(endpoint.TargetRequestFullName!, out request);
        if (request is null && !string.IsNullOrEmpty(endpoint.TargetRequestName))
            byName.TryGetValue(endpoint.TargetRequestName, out request);

        var target = string.IsNullOrEmpty(endpoint.TargetRequestName)
            ? "unresolved"
            : endpoint.TargetRequestName;
        var handler = request is null || request.Handlers.Count == 0
            ? "—"
            : string.Join(", ", request.Handlers
                .Select(MermaidDiagramText.ShortTypeName)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(h => h, StringComparer.Ordinal));

        return new Row(
            string.IsNullOrEmpty(endpoint.HttpMethod) ? "—" : endpoint.HttpMethod,
            string.IsNullOrEmpty(endpoint.Route) ? "—" : endpoint.Route,
            endpoint.Name,
            endpoint.ProjectName ?? MermaidDiagramText.UnassignedProject,
            target,
            handler,
            endpoint.IsInferred);
    }
}
