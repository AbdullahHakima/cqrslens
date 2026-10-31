namespace CqrsLens;

/// <summary>
/// Reports Clean Architecture layer violations and circular project dependencies.
/// </summary>
internal static class ProjectLayerChecks
{
    /// <summary>Layers a Domain project must never depend on.</summary>
    private static readonly string[] ForbiddenForDomain = { "Infrastructure", "Api", "Web", "UI" };

    /// <summary>Layers an Application project must never depend on.</summary>
    private static readonly string[] ForbiddenForApplication = { "Infrastructure", "Api", "Web", "UI" };

    internal static void Run(
        AnalysisResult result,
        IReadOnlyDictionary<string, List<ProjectInfo>> projectsByName,
        List<CheckFinding> findings)
    {
        ReportLayerViolations(result, projectsByName, findings);
        ReportCircularDependencies(result, projectsByName, findings);
    }

    private static void ReportLayerViolations(
        AnalysisResult result,
        IReadOnlyDictionary<string, List<ProjectInfo>> projectsByName,
        List<CheckFinding> findings)
    {
        // Layer labels are always name-inferred. On a framework-scale solution the
        // handful of layer-named projects are coincidental, e.g. a
        // navigation-contracts library whose name contains "UI" or a framework
        // target project whose name contains "Application", and the intended
        // reference between them is not a violation. When convention classification
        // does not describe the solution, an outward reference is still reported but
        // as a warning: uncertainty in the layer model must not fail --strict. A
        // conventionally-layered solution keeps error severity.
        var layerModelAsserted =
            result.HasConfiguredLayers || LayerModelConfidence.IsAsserted(result.Projects);
        var violationSeverity = layerModelAsserted ? CheckSeverity.Error : CheckSeverity.Warning;

        // Tests are not production layers and may legitimately reference anything.
        foreach (var project in result.Projects)
        {
            var isDomain = string.Equals(project.Layer, "Domain", StringComparison.OrdinalIgnoreCase);
            var isApplication = string.Equals(project.Layer, "Application", StringComparison.OrdinalIgnoreCase);
            if (!isDomain && !isApplication)
                continue;

            var forbidden = isDomain ? ForbiddenForDomain : ForbiddenForApplication;
            var code = isDomain
                ? ArchitectureChecker.LayerViolationCode
                : ArchitectureChecker.ApplicationLayerViolationCode;
            var layerLabel = isDomain ? "Domain" : "Application";

            foreach (var reference in project.ReferenceNames)
            {
                // A duplicate project name makes the reference target ambiguous; do not
                // guess and report a false layer violation.
                if (projectsByName.TryGetValue(reference, out var targets) &&
                    targets.Count == 1 &&
                    forbidden.Contains(targets[0].Layer, StringComparer.OrdinalIgnoreCase))
                {
                    findings.Add(new CheckFinding(
                        violationSeverity,
                        code,
                        $"{layerLabel} project `{project.Name}` references {targets[0].Layer} project `{targets[0].Name}`.",
                        Target: $"{project.Name} -> {targets[0].Name}",
                        Location: SourceLocation.FromLine(project.FilePath, result.RootPath, 1, 1),
                        ProjectName: project.Name));
                }
            }
        }
    }

    private static void ReportCircularDependencies(
        AnalysisResult result,
        IReadOnlyDictionary<string, List<ProjectInfo>> projectsByName,
        List<CheckFinding> findings)
    {
        var uniqueProjects = projectsByName
            .Where(kv => kv.Value.Count == 1)
            .ToDictionary(kv => kv.Key, kv => kv.Value[0], StringComparer.OrdinalIgnoreCase);

        var reportedCycles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var path = new List<string>();
        var onStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Dfs(string current)
        {
            path.Add(current);
            onStack.Add(current);

            if (uniqueProjects.TryGetValue(current, out var project))
            {
                foreach (var dep in project.ReferenceNames.OrderBy(r => r, StringComparer.OrdinalIgnoreCase))
                {
                    if (!uniqueProjects.TryGetValue(dep, out var target))
                        continue;

                    if (onStack.Contains(target.Name))
                    {
                        RecordCycle(result, path, target, uniqueProjects, reportedCycles, findings);
                    }
                    else if (!path.Contains(target.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        Dfs(target.Name);
                    }
                }
            }

            onStack.Remove(current);
            path.RemoveAt(path.Count - 1);
        }

        foreach (var name in uniqueProjects.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
            Dfs(name);
    }

    private static void RecordCycle(
        AnalysisResult result,
        List<string> path,
        ProjectInfo target,
        IReadOnlyDictionary<string, ProjectInfo> uniqueProjects,
        HashSet<string> reportedCycles,
        List<CheckFinding> findings)
    {
        var idx = path.FindIndex(p => string.Equals(p, target.Name, StringComparison.OrdinalIgnoreCase));
        if (idx < 0)
            return;

        var ring = path.Skip(idx).ToList();
        var minIndex = 0;
        for (var i = 1; i < ring.Count; i++)
        {
            if (string.Compare(ring[i], ring[minIndex], StringComparison.OrdinalIgnoreCase) < 0)
                minIndex = i;
        }

        var canonical = ring.Skip(minIndex)
            .Concat(ring.Take(minIndex))
            .ToList();
        canonical.Add(canonical[0]);
        var cycleKey = string.Join(" -> ", canonical);
        if (!reportedCycles.Add(cycleKey))
            return;

        var owner = uniqueProjects[canonical[0]];
        findings.Add(new CheckFinding(
            CheckSeverity.Error,
            ArchitectureChecker.CircularProjectDependencyCode,
            $"Circular project dependency detected: {cycleKey}.",
            Target: cycleKey,
            Location: SourceLocation.FromLine(owner.FilePath, result.RootPath, 1, 1),
            ProjectName: owner.Name));
    }
}
