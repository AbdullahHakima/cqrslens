namespace CqrsLens;

/// <summary>Builds project dependency and layer diagrams.</summary>
internal static class ProjectDiagramBuilder
{
    internal static string? ProjectGraph(AnalysisResult result)
        => result.Projects.Count == 0
            ? null
            : ProjectGraphWriter.Write(ProjectDiagramData.Create(result));

    internal static string? LayerGraph(AnalysisResult result)
        => result.Projects.Count == 0
            ? null
            : LayerGraphWriter.Write(ProjectDiagramData.Create(result));
}
