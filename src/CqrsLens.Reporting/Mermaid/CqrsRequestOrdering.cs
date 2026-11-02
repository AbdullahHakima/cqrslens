namespace CqrsLens;

/// <summary>Canonical request ordering and project grouping for CQRS diagrams.</summary>
internal static class CqrsRequestOrdering
{
    internal static List<CqrsRequest> OrderRequests(IEnumerable<CqrsRequest> requests)
        => requests
            // Feature first so a feature's commands and queries stay together when a
            // project is split into parts; then command before query, then name.
            .OrderBy(r => FeatureName(r), StringComparer.Ordinal)
            .ThenBy(KindOrder)
            .ThenBy(r => r.FullName, StringComparer.Ordinal)
            .ToList();

    internal static string ProjectKey(CqrsRequest request)
        => string.IsNullOrEmpty(request.ProjectName)
            ? MermaidDiagramText.UnassignedProject
            : request.ProjectName;

    /// <summary>
    /// Namespace segments that name a structural folder rather than a feature. They
    /// are skipped from both ends when deriving a request's feature.
    /// </summary>
    private static readonly HashSet<string> StructuralSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "Application", "Domain", "Infrastructure", "Api", "Presentation", "Web", "UI", "Src", "Source",
        "Features", "UseCases", "Commands", "Queries", "Requests", "Handlers",
        "Command", "Query", "Request", "Handler",
    };

    private static readonly HashSet<string> LayerBoundarySegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "Application", "Domain", "Infrastructure", "Api", "Presentation", "Web", "UI",
    };

    /// <summary>
    /// Feature key for the opt-in feature split: the project followed by the first
    /// meaningful namespace segment below it. Grouping by project keeps two modules
    /// that happen to share a feature name in separate diagrams.
    /// </summary>
    internal static string FeatureKey(CqrsRequest request)
        => FeatureKey(request.ProjectName, request.Namespace);

    internal static string FeatureKey(string? project, string? ns)
        => $"{(string.IsNullOrEmpty(project) ? MermaidDiagramText.UnassignedProject : project)} :: {FeatureName(ns, project)}";

    /// <summary>
    /// The feature segment a request lives in: the first namespace segment below the
    /// declaring project after leading/trailing structural segments are removed.
    /// Searches for <c>{projectName}</c> anywhere in the namespace so solution-prefixed
    /// namespaces (e.g. <c>Northwind.Application.Customers...</c> in project <c>Application</c>)
    /// do not collapse to the solution prefix. Falls back to the project name when the
    /// namespace holds no feature segment.
    /// </summary>
    internal static string FeatureName(CqrsRequest request)
        => FeatureName(request.Namespace, request.ProjectName);

    internal static string FeatureName(string? ns, string? project)
    {
        var segments = (ns ?? "")
            .Split('.', StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        if (!string.IsNullOrEmpty(project))
        {
            var projectSegments = project.Split('.', StringSplitOptions.RemoveEmptyEntries);
            var matchedIndex = FindSubsequenceIndex(segments, projectSegments);
            if (matchedIndex >= 0)
            {
                segments = segments.Skip(matchedIndex + projectSegments.Length).ToList();
            }
            else
            {
                var layerIdx = segments.FindIndex(LayerBoundarySegments.Contains);
                if (layerIdx >= 0)
                    segments = segments.Skip(layerIdx + 1).ToList();
            }
        }
        else
        {
            var layerIdx = segments.FindIndex(LayerBoundarySegments.Contains);
            if (layerIdx >= 0)
                segments = segments.Skip(layerIdx + 1).ToList();
        }

        while (segments.Count > 0 && StructuralSegments.Contains(segments[0]))
            segments.RemoveAt(0);
        while (segments.Count > 0 && StructuralSegments.Contains(segments[^1]))
            segments.RemoveAt(segments.Count - 1);

        if (segments.Count > 0)
            return segments[0];

        return string.IsNullOrEmpty(project) ? MermaidDiagramText.UnassignedProject : project;
    }

    private static int FindSubsequenceIndex(List<string> segments, string[] target)
    {
        if (target.Length == 0 || segments.Count < target.Length)
            return -1;

        for (var i = 0; i <= segments.Count - target.Length; i++)
        {
            var match = true;
            for (var j = 0; j < target.Length; j++)
            {
                if (!string.Equals(segments[i + j], target[j], StringComparison.OrdinalIgnoreCase))
                {
                    match = false;
                    break;
                }
            }
            if (match)
                return i;
        }

        return -1;
    }

    /// <summary>Human-readable heading for a feature split part.</summary>
    internal static string FeatureHeading(CqrsRequest request)
        => FeatureHeading(request.ProjectName, request.Namespace);

    internal static string FeatureHeading(string? project, string? ns)
    {
        var projectLabel = string.IsNullOrEmpty(project) ? MermaidDiagramText.UnassignedProject : project;
        var feature = FeatureName(ns, project);
        return string.Equals(projectLabel, feature, StringComparison.Ordinal)
            ? feature
            : $"{projectLabel} — {feature}";
    }

    private static int KindOrder(CqrsRequest request)
        => request.Kind switch
        {
            "Command" => 0,
            "Query" => 1,
            _ => 2,
        };
}
