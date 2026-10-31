namespace CqrsLens;

/// <summary>Canonical request ordering and project grouping for CQRS diagrams.</summary>
internal static class CqrsRequestOrdering
{
    internal static List<CqrsRequest> OrderRequests(IEnumerable<CqrsRequest> requests)
        => requests
            .OrderBy(KindOrder)
            .ThenBy(r => r.FullName, StringComparer.Ordinal)
            .ToList();

    internal static string ProjectKey(CqrsRequest request)
        => string.IsNullOrEmpty(request.ProjectName)
            ? MermaidDiagramText.UnassignedProject
            : request.ProjectName;

    private static int KindOrder(CqrsRequest request)
        => request.Kind switch
        {
            "Command" => 0,
            "Query" => 1,
            _ => 2,
        };
}
