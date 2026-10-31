using System.Text;

namespace CqrsLens;

/// <summary>Emits shared project edges, link styles and layer class definitions.</summary>
internal static class ProjectDiagramSyntax
{
    internal static List<int> EmitEdges(
        StringBuilder sb,
        ProjectDiagramData data,
        IReadOnlyDictionary<string, string> projectIds)
    {
        var linkIndexes = new List<int>();
        var edgeIndex = 0;
        foreach (var project in data.Projects)
        {
            foreach (var dependency in project.ReferenceNames
                         .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(d => d, StringComparer.Ordinal))
            {
                if (!data.ProjectsByName.TryGetValue(dependency, out var target) || target is null)
                    continue;

                var violation = data.Violations.Contains((project.Name, target.Name));
                var label = violation ? "|violates|" : "";
                sb.AppendLine(
                    $"    {projectIds[project.FilePath]} -->{label} {projectIds[target.FilePath]}");
                if (violation)
                    linkIndexes.Add(edgeIndex);
                edgeIndex++;
            }
        }

        return linkIndexes;
    }

    internal static void EmitLinkStyles(StringBuilder sb, IEnumerable<int> indexes)
    {
        foreach (var index in indexes)
            sb.AppendLine(
                $"    linkStyle {index} stroke:#dc2626,stroke-width:2px,stroke-dasharray:4 3");
    }

    internal static void EmitLayerClassDefs(StringBuilder sb)
    {
        sb.AppendLine("    classDef clsLayerDomain fill:#ecfdf5,stroke:#059669,stroke-width:2px,color:#064e3b");
        sb.AppendLine("    classDef clsLayerApplication fill:#eff6ff,stroke:#2563eb,stroke-width:2px,color:#1e3a8a");
        sb.AppendLine("    classDef clsLayerInfrastructure fill:#fff7ed,stroke:#c2410c,stroke-width:2px,color:#431407");
        sb.AppendLine("    classDef clsLayerApi fill:#f5f3ff,stroke:#7c3aed,stroke-width:2px,color:#2e1065");
        sb.AppendLine("    classDef clsLayerTests fill:#f8fafc,stroke:#64748b,stroke-width:1.5px,color:#1e293b");
        sb.AppendLine("    classDef clsLayerOther fill:#f8fafc,stroke:#64748b,stroke-width:1.5px,color:#1e293b");
    }

    internal static string LayerClass(string layer)
        => layer.ToLowerInvariant() switch
        {
            "domain" => "clsLayerDomain",
            "application" => "clsLayerApplication",
            "infrastructure" => "clsLayerInfrastructure",
            "api" or "web" or "ui" => "clsLayerApi",
            "tests" => "clsLayerTests",
            _ => "clsLayerOther",
        };
}
