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

    internal static IReadOnlyList<DiagramSection> CqrsMapSections(AnalysisResult result, CqrsMapSplitMode split)
        => CqrsDiagramPlanner.Sections(result, split);

    internal static string ShortNodeLabel(string fullName, IReadOnlyCollection<string> peerFullNames)
    {
        var shortName = MermaidDiagramText.ShortTypeName(fullName);
        var sameShortCount = peerFullNames.Count(p =>
            string.Equals(MermaidDiagramText.ShortTypeName(p), shortName, StringComparison.Ordinal));
        if (sameShortCount <= 1)
            return shortName;

        return fullName.Count(c => c == '.') <= 2
            ? fullName
            : MermaidDiagramText.ShortTypeName(fullName, peerFullNames);
    }
}
