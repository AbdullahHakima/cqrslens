using System.Text;

namespace CqrsLens;

/// <summary>Writes the layer-grouped Mermaid flowchart.</summary>
internal static class LayerGraphWriter
{
    internal static string Write(ProjectDiagramData data)
    {
        var projectsByLayer = data.Projects
            .GroupBy(p => p.Layer, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        // Outer dependents on top, core at the bottom, so dependency arrows flow
        // consistently top-to-bottom: Tests → Api/Web/UI → ad-hoc folders (Src) →
        // Infrastructure → Application → Domain. ProjectInfo.Layer is not changed.
        var layers = OrderLayersOuterToCore(data.Projects.Select(p => p.Layer)).ToList();
        var ids = new MermaidIdAllocator();
        var projectIds = data.Projects.ToDictionary(
            p => p.FilePath,
            p => ids.Allocate("project:" + p.FilePath, "prj", data.Labels[p.FilePath]),
            StringComparer.Ordinal);
        var layerIds = layers.ToDictionary(
            layer => layer,
            layer => ids.Allocate("layer:" + layer, "lyr", layer),
            StringComparer.Ordinal);

        var sb = new StringBuilder();
        sb.AppendLine("flowchart TD");
        sb.AppendLine("    accTitle: Layer diagram");
        sb.AppendLine(
            $"    accDescr: {data.Projects.Count} projects, {layers.Count} layers, " +
            $"{data.ReferenceCount} references.");
        foreach (var layer in layers)
        {
            sb.AppendLine($"    subgraph {layerIds[layer]}[\"{MermaidDiagramText.MermaidLabel(layer)}\"]");
            foreach (var project in projectsByLayer[layer])
                sb.AppendLine(
                    $"        {projectIds[project.FilePath]}[\"{MermaidDiagramText.MermaidLabel(data.Labels[project.FilePath])}\"]" +
                    $":::{ProjectDiagramSyntax.LayerClass(project.Layer)}");
            sb.AppendLine("    end");
        }

        var linkIndexes = ProjectDiagramSyntax.EmitEdges(sb, data, projectIds);
        foreach (var layer in layers)
            sb.AppendLine(
                $"    style {layerIds[layer]} fill:none,stroke:#94a3b8,stroke-dasharray:4 3");
        ProjectDiagramSyntax.EmitLinkStyles(sb, linkIndexes);
        ProjectDiagramSyntax.EmitLayerClassDefs(sb);
        return sb.ToString().TrimEnd();
    }

    private static IEnumerable<string> OrderLayersOuterToCore(IEnumerable<string> layers)
        => layers
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(LayoutRank)
            .ThenBy(layer => layer, StringComparer.OrdinalIgnoreCase);

    private static int LayoutRank(string layer)
    {
        if (string.Equals(layer, "Tests", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (layer.Equals("Api", StringComparison.OrdinalIgnoreCase) ||
            layer.Equals("Web", StringComparison.OrdinalIgnoreCase) ||
            layer.Equals("UI", StringComparison.OrdinalIgnoreCase))
            return 1;
        if (layer.Equals("Infrastructure", StringComparison.OrdinalIgnoreCase))
            return 3;
        if (layer.Equals("Application", StringComparison.OrdinalIgnoreCase))
            return 4;
        if (layer.Equals("Domain", StringComparison.OrdinalIgnoreCase))
            return 5;
        return 2; // ad-hoc folders such as Src, Persistence, Common
    }
}
