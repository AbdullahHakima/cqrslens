namespace CqrsLens;

/// <summary>Precomputed project metadata shared by the project and layer diagrams.</summary>
internal sealed class ProjectDiagramData
{
    private ProjectDiagramData(
        IReadOnlyList<ProjectInfo> projects,
        IReadOnlyDictionary<string, string> labels,
        IReadOnlyDictionary<string, ProjectInfo?> projectsByName,
        int referenceCount,
        HashSet<(string Source, string Target)> violations)
    {
        Projects = projects;
        Labels = labels;
        ProjectsByName = projectsByName;
        ReferenceCount = referenceCount;
        Violations = violations;
    }

    public IReadOnlyList<ProjectInfo> Projects { get; }
    public IReadOnlyDictionary<string, string> Labels { get; }
    public IReadOnlyDictionary<string, ProjectInfo?> ProjectsByName { get; }
    public int ReferenceCount { get; }
    public HashSet<(string Source, string Target)> Violations { get; }

    internal static ProjectDiagramData Create(AnalysisResult result)
    {
        var projects = OrderedProjects(result);
        var projectsByName = UniqueProjectsByName(projects);
        return new ProjectDiagramData(
            projects,
            ProjectLabels(result, projects),
            projectsByName,
            ResolvedReferenceCount(projects, projectsByName),
            LayerViolationPairs(result.Checks));
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
}
