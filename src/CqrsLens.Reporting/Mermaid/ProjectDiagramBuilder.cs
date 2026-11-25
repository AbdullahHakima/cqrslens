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

    /// <summary>
    /// Project graph with optional test exclusion and focus (focused project plus its direct
    /// reference/integration neighbors). Without filters the output is identical to
    /// <see cref="ProjectGraph(AnalysisResult)"/>.
    /// </summary>
    internal static string? ProjectGraph(AnalysisResult result, bool excludeTests, string? focusService)
    {
        var projects = SystemViewFilter.FilterProjects(result, excludeTests, focusService);
        if (projects is null)
            return ProjectGraph(result);
        return projects.Count == 0
            ? null
            : ProjectGraphWriter.Write(ProjectDiagramData.Create(result, projects));
    }

    /// <summary>Layer graph with the same filtering rules as the filtered project graph.</summary>
    internal static string? LayerGraph(AnalysisResult result, bool excludeTests, string? focusService)
    {
        var projects = SystemViewFilter.FilterProjects(result, excludeTests, focusService);
        if (projects is null)
            return LayerGraph(result);
        return projects.Count == 0
            ? null
            : LayerGraphWriter.Write(ProjectDiagramData.Create(result, projects));
    }
}
