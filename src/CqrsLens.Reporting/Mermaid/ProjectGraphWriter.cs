using System.Text;

namespace CqrsLens;

/// <summary>Writes the project dependency Mermaid flowchart.</summary>
internal static class ProjectGraphWriter
{
    internal static string Write(ProjectDiagramData data)
    {
        var ids = new MermaidIdAllocator();
        var projectIds = data.Projects.ToDictionary(
            p => p.FilePath,
            p => ids.Allocate("project:" + p.FilePath, "prj", data.Labels[p.FilePath]),
            StringComparer.Ordinal);

        var sb = new StringBuilder();
        sb.AppendLine("flowchart TD");
        sb.AppendLine("    accTitle: Project dependency graph");
        sb.AppendLine(
            $"    accDescr: {data.Projects.Count} projects, {data.ReferenceCount} references.");
        foreach (var project in data.Projects)
            sb.AppendLine(
                $"    {projectIds[project.FilePath]}[\"{MermaidDiagramText.MermaidLabel(data.Labels[project.FilePath])}\"]" +
                $":::{ProjectDiagramSyntax.LayerClass(project.Layer)}");

        var linkIndexes = ProjectDiagramSyntax.EmitEdges(sb, data, projectIds);
        ProjectDiagramSyntax.EmitLinkStyles(sb, linkIndexes);
        ProjectDiagramSyntax.EmitLayerClassDefs(sb);
        return sb.ToString().TrimEnd();
    }
}
