using System.Text;
using static CqrsLens.MermaidDiagramText;

namespace CqrsLens;

/// <summary>Builds project dependency and layer diagrams.</summary>
internal static class ProjectDiagramBuilder
{
    internal static string? ProjectGraph(AnalysisResult result)
    {
        if (result.Projects.Count == 0) return null;

        var projects = OrderedProjects(result);
        var labels = ProjectLabels(result, projects);
        var projectsByName = UniqueProjectsByName(projects);
        var referenceCount = ResolvedReferenceCount(projects, projectsByName);
        var violations = LayerViolationPairs(result.Checks);
        var ids = new MermaidIdAllocator();
        var projectIds = projects.ToDictionary(
            p => p.FilePath,
            p => ids.Allocate("project:" + p.FilePath, "prj", labels[p.FilePath]),
            StringComparer.Ordinal);

        var sb = new StringBuilder();
        sb.AppendLine("flowchart TD");
        sb.AppendLine("    accTitle: Project dependency graph");
        sb.AppendLine(
            $"    accDescr: {projects.Count} projects, {referenceCount} references.");
        foreach (var project in projects)
            sb.AppendLine(
                $"    {projectIds[project.FilePath]}[\"{MermaidLabel(labels[project.FilePath])}\"]" +
                $":::{LayerClass(project.Layer)}");

        var linkIndexes = EmitProjectEdges(sb, projects, projectsByName, projectIds, violations);
        EmitLinkStyles(sb, linkIndexes);
        EmitLayerClassDefs(sb);
        return sb.ToString().TrimEnd();
    }

    internal static string? LayerGraph(AnalysisResult result)
    {
        if (result.Projects.Count == 0) return null;

        var projects = OrderedProjects(result);
        var labels = ProjectLabels(result, projects);
        var projectsByName = UniqueProjectsByName(projects);
        var referenceCount = ResolvedReferenceCount(projects, projectsByName);
        var violations = LayerViolationPairs(result.Checks);
        var projectsByLayer = projects
            .GroupBy(p => p.Layer, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var layers = LayerClassifier.OrderLayers(projects.Select(p => p.Layer)).ToList();
        var ids = new MermaidIdAllocator();
        var projectIds = projects.ToDictionary(
            p => p.FilePath,
            p => ids.Allocate("project:" + p.FilePath, "prj", labels[p.FilePath]),
            StringComparer.Ordinal);
        var layerIds = layers.ToDictionary(
            layer => layer,
            layer => ids.Allocate("layer:" + layer, "lyr", layer),
            StringComparer.Ordinal);

        var sb = new StringBuilder();
        sb.AppendLine("flowchart TD");
        sb.AppendLine("    accTitle: Layer diagram");
        sb.AppendLine(
            $"    accDescr: {projects.Count} projects, {layers.Count} layers, " +
            $"{referenceCount} references.");
        foreach (var layer in layers)
        {
            sb.AppendLine($"    subgraph {layerIds[layer]}[\"{MermaidLabel(layer)}\"]");
            foreach (var project in projectsByLayer[layer])
                sb.AppendLine(
                    $"        {projectIds[project.FilePath]}[\"{MermaidLabel(labels[project.FilePath])}\"]" +
                    $":::{LayerClass(project.Layer)}");
            sb.AppendLine("    end");
        }

        var linkIndexes = EmitProjectEdges(sb, projects, projectsByName, projectIds, violations);
        foreach (var layer in layers)
            sb.AppendLine(
                $"    style {layerIds[layer]} fill:none,stroke:#94a3b8,stroke-dasharray:4 3");
        EmitLinkStyles(sb, linkIndexes);
        EmitLayerClassDefs(sb);
        return sb.ToString().TrimEnd();
    }

    private static List<ProjectInfo> OrderedProjects(AnalysisResult result)
        => result.Projects
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.FilePath, StringComparer.Ordinal)
            .ToList();

    private static Dictionary<string, string> ProjectLabels(
        AnalysisResult result,
        IReadOnlyList<ProjectInfo> projects)
    {
        var nameCounts = projects
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var project in projects)
        {
            labels[project.FilePath] = nameCounts[project.Name] == 1
                ? project.Name
                : DisambiguatedProjectLabel(result, project);
        }

        return labels;
    }

    private static string DisambiguatedProjectLabel(AnalysisResult result, ProjectInfo project)
    {
        var relative = Path.GetRelativePath(result.RootPath, project.DirectoryPath)
            .Replace(Path.DirectorySeparatorChar, '/');
        if (relative is "." or "")
            relative = Path.GetFileName(project.FilePath);
        return $"{project.Name} ({relative})";
    }

    private static Dictionary<string, ProjectInfo?> UniqueProjectsByName(
        IReadOnlyList<ProjectInfo> projects)
        => projects
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Count() == 1 ? g.Single() : null,
                StringComparer.OrdinalIgnoreCase);

    private static int ResolvedReferenceCount(
        IReadOnlyList<ProjectInfo> projects,
        IReadOnlyDictionary<string, ProjectInfo?> projectsByName)
        => projects.Sum(project => project.ReferenceNames.Count(dependency =>
            projectsByName.TryGetValue(dependency, out var target) && target is not null));

    private static HashSet<(string Source, string Target)> LayerViolationPairs(
        IEnumerable<CheckFinding> checks)
    {
        var pairs = new HashSet<(string, string)>();
        foreach (var check in checks)
        {
            if ((check.Code != ArchitectureChecker.LayerViolationCode &&
                 check.Code != ArchitectureChecker.ApplicationLayerViolationCode) ||
                string.IsNullOrEmpty(check.Target))
                continue;

            var separator = check.Target.IndexOf(" -> ", StringComparison.Ordinal);
            if (separator > 0 && separator + 4 < check.Target.Length)
                pairs.Add((check.Target[..separator], check.Target[(separator + 4)..]));
        }

        return pairs;
    }

    private static List<int> EmitProjectEdges(
        StringBuilder sb,
        IReadOnlyList<ProjectInfo> projects,
        IReadOnlyDictionary<string, ProjectInfo?> projectsByName,
        IReadOnlyDictionary<string, string> projectIds,
        HashSet<(string Source, string Target)> violations)
    {
        var linkIndexes = new List<int>();
        var edgeIndex = 0;
        foreach (var project in projects)
        {
            foreach (var dependency in project.ReferenceNames
                         .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(d => d, StringComparer.Ordinal))
            {
                if (!projectsByName.TryGetValue(dependency, out var target) || target is null)
                    continue;

                var violation = violations.Contains((project.Name, target.Name));
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

    private static void EmitLinkStyles(StringBuilder sb, IEnumerable<int> indexes)
    {
        foreach (var index in indexes)
            sb.AppendLine(
                $"    linkStyle {index} stroke:#dc2626,stroke-width:2px,stroke-dasharray:4 3");
    }

    private static void EmitLayerClassDefs(StringBuilder sb)
    {
        sb.AppendLine("    classDef clsLayerDomain fill:#ecfdf5,stroke:#059669,stroke-width:2px,color:#064e3b");
        sb.AppendLine("    classDef clsLayerApplication fill:#eff6ff,stroke:#2563eb,stroke-width:2px,color:#1e3a8a");
        sb.AppendLine("    classDef clsLayerInfrastructure fill:#fff7ed,stroke:#c2410c,stroke-width:2px,color:#431407");
        sb.AppendLine("    classDef clsLayerApi fill:#f5f3ff,stroke:#7c3aed,stroke-width:2px,color:#2e1065");
        sb.AppendLine("    classDef clsLayerTests fill:#f8fafc,stroke:#64748b,stroke-width:1.5px,color:#1e293b");
        sb.AppendLine("    classDef clsLayerOther fill:#f8fafc,stroke:#64748b,stroke-width:1.5px,color:#1e293b");
    }

    private static string LayerClass(string layer)
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