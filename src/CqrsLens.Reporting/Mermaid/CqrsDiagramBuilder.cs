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

    internal static IReadOnlyList<DiagramSection> CqrsMapSections(AnalysisResult result)
        => CqrsDiagramPlanner.Sections(result);
}
