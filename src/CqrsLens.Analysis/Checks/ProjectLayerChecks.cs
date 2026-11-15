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
            result.HasConfiguredLayers ||
            result.Spine.Projects.Any(p => p.LayerProvenance == LayerProvenance.Configured) ||
            LayerModelConfidence.IsAsserted(result.Projects);
        var violationSeverity = layerModelAsserted ? CheckSeverity.Error : CheckSeverity.Warning;

        // Tests are not production layers and may legitimately reference anything.
        var relBuffer = new List<SpineRelation>();
        foreach (var project in result.Spine.Projects)
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

            result.Spine.CollectOutgoingRelationsUnordered(
                NodeReference.ForProject(project.Id),
                RelationKind.References,
                relBuffer);
            for (var i = 0; i < relBuffer.Count; i++)
            {
                var rel = relBuffer[i];
                var targetName = rel.To.ProjectId is not null
                    ? result.IdentityResolver.ProjectNameFor(rel.To.ProjectId)
                    : rel.To.Name;
                if (targetName is null)
                    continue;

                // A duplicate project name makes the reference target ambiguous; do not
                // guess and report a false layer violation.
                if (projectsByName.TryGetValue(targetName, out var targets) &&
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
        var relBuffer = new List<SpineRelation>();
        var depsByProject = new Dictionary<string, ProjectInfo[]>(uniqueProjects.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var kv in uniqueProjects)
        {
            if (kv.Value.SpineId is null)
                continue;

            result.Spine.CollectOutgoingRelationsUnordered(
                NodeReference.ForProject(kv.Value.SpineId),
                RelationKind.References,
                relBuffer);
            if (relBuffer.Count == 0)
                continue;

            var deps = new string[relBuffer.Count];
            for (var i = 0; i < relBuffer.Count; i++)
            {
                var rel = relBuffer[i];
                deps[i] = rel.To.ProjectId is not null
                    ? result.IdentityResolver.ProjectNameFor(rel.To.ProjectId) ?? rel.To.Name
                    : rel.To.Name;
            }
            if (deps.Length > 1)
                Array.Sort(deps, StringComparer.OrdinalIgnoreCase);

            var targets = new List<ProjectInfo>(deps.Length);
            for (var i = 0; i < deps.Length; i++)
            {
                if (uniqueProjects.TryGetValue(deps[i], out var target))
                    targets.Add(target);
            }
            if (targets.Count > 0)
                depsByProject[kv.Key] = targets.ToArray();
        }

        void Dfs(string current)
        {
            path.Add(current);
            onStack.Add(current);

            if (depsByProject.TryGetValue(current, out var targets))
            {
                for (var i = 0; i < targets.Length; i++)
                {
                    var target = targets[i];
                    if (onStack.Contains(target.Name))
                    {
                        RecordCycle(result, path, target, uniqueProjects, reportedCycles, findings);
                    }
                    else
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
