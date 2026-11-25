namespace CqrsLens;

/// <summary>Evaluates planned project changes against the scan's project graph without mutating it.</summary>
public static class ArchitectureProposalEvaluator
{
    public static ArchitectureProposalEvaluation Evaluate(
        AnalysisResult result,
        IReadOnlyList<ArchitectureProposalProject> projects,
        IReadOnlyList<ArchitectureProposalReference> references)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(references);

        var findings = new List<ArchitectureProposalFinding>();
        ArchitectureProposalEvaluation Finish(
            string status,
            IReadOnlyList<ArchitectureProposalProjectMetrics>? metrics = null,
            ArchitectureProposalBlastRadius? blastRadius = null) => new(
            status, result.Projects.Count, projects.ToArray(), references.ToArray(), findings.ToArray(),
            Metrics: metrics, BlastRadius: blastRadius);

        if (projects.Count == 0 && references.Count == 0)
        {
            findings.Add(new("EmptyProposal", "error", "The proposal has no changes.",
                "Add a planned project or a project reference change."));
            return Finish("invalid");
        }

        var projectsByName = ProjectNameIndex.Build(result);
        var projectsByPath = result.Projects.Where(project => !string.IsNullOrEmpty(project.FilePath))
            .ToLookup(project => RelativeFile(project, result.RootPath)!, StringComparer.OrdinalIgnoreCase);
        var proposedProjectInfos = AddProposedProjects(projects, projectsByName, result.RootPath, findings);
        var changes = ResolveReferenceChanges(references, projectsByName, projectsByPath, result.RootPath, findings);
        if (findings.Any(finding => finding.Severity == "error"))
            return Finish("invalid");

        var beforeGraph = new ArchitectureProposalGraph(result.Projects, projectsByName);
        var afterGraph = new ArchitectureProposalGraph(result.Projects, projectsByName);
        var added = new List<(ProjectInfo From, ProjectInfo To)>();
        var removed = new List<(ProjectInfo From, ProjectInfo To)>();
        var conditionalWarned = new HashSet<(ProjectInfo Project, string Include, string Condition)>();

        if (changes.Count > 0)
        {
            foreach (var (edge, change) in changes)
            {
                var (from, to) = edge;
                var isAdd = change.Operation == "add";
                if (isAdd ? afterGraph.Add(from, to) : afterGraph.Remove(from, to))
                {
                    if (isAdd)
                        added.Add(edge);
                    else
                        removed.Add(edge);

                    SurfaceConditionalReferences(from, change, projectsByName, result.RootPath, conditionalWarned, findings);
                    SurfaceConditionalReferences(to, change, projectsByName, result.RootPath, conditionalWarned, findings);
                    continue;
                }

                var exists = isAdd ? "already exists" : "is absent from the observed graph";
                findings.Add(new("UnchangedReference", "warning",
                    $"Reference {change.From} -> {change.To} {exists}.",
                    "Confirm the current graph and omit the change if it is already in the desired state.",
                    File: RelativeFile(from, result.RootPath), Reference: change));
            }

            var layerModelAsserted = result.HasConfiguredLayers || LayerModelConfidence.IsAsserted(result.Projects);
            var reportedTransitive = new HashSet<(ProjectInfo Inner, ProjectInfo Outer)>();
            foreach (var (from, to) in added)
            {
                var change = changes[(from, to)];
                CheckLayerRule(result, from, to, change, layerModelAsserted, findings);
                CheckForCycle(result, afterGraph, from, to, change, projectsByName, conditionalWarned, findings);
                CheckTransitiveLayerViolations(
                    result, beforeGraph, afterGraph, from, to, change,
                    layerModelAsserted, projectsByName, reportedTransitive, findings);
            }
        }

        var metrics = ComputeMetrics(projectsByName, proposedProjectInfos, beforeGraph, afterGraph, result.RootPath);
        var blastRadius = ComputeBlastRadius(result, projectsByName, proposedProjectInfos, added, removed, beforeGraph);

        if (findings.Any(finding => finding.Severity == "error"))
            return Finish("conflicts", metrics, blastRadius);

        return Finish(findings.Count > 0 ? "review" : "checks_passed", metrics, blastRadius);
    }

    private static HashSet<ProjectInfo> AddProposedProjects(
        IReadOnlyList<ArchitectureProposalProject> projects,
        Dictionary<string, List<ProjectInfo>> projectsByName,
        string rootPath,
        List<ArchitectureProposalFinding> findings)
    {
        var proposed = new HashSet<ProjectInfo>();
        foreach (var project in projects)
        {
            if (project is null || string.IsNullOrWhiteSpace(project.Name) || string.IsNullOrWhiteSpace(project.Layer))
            {
                findings.Add(new("InvalidProject", "error", "A proposed project needs a name and layer.",
                    "Supply the intended project name and its architectural layer."));
                continue;
            }

            var name = project.Name.Trim();
            if (projectsByName.TryGetValue(name, out var candidates))
            {
                findings.Add(new("DuplicateProject", "error", $"The name {name} is already in use.",
                    "Use the existing project as a reference endpoint or choose a distinct project name.",
                    Candidates: CandidateFiles(candidates, rootPath)));
                continue;
            }

            var layer = project.Layer.Trim();
            if (!LayerClassifier.LayerOrder.Contains(layer, StringComparer.OrdinalIgnoreCase) &&
                !string.Equals(layer, "Other", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new("UnknownLayerName", "warning",
                    $"Proposed project {name} uses unrecognized layer '{layer}'.",
                    $"Use a standard layer ({string.Join(", ", LayerClassifier.LayerOrder)}, or Other) so layer checks evaluate it accurately."));
            }

            var info = new ProjectInfo
            {
                Name = name,
                Layer = layer,
                DirectoryPath = string.Empty,
                FilePath = string.Empty,
            };
            projectsByName.Add(name, [info]);
            proposed.Add(info);
        }

        return proposed;
    }

    private static Dictionary<(ProjectInfo From, ProjectInfo To), ArchitectureProposalReference> ResolveReferenceChanges(
        IReadOnlyList<ArchitectureProposalReference> references,
        IReadOnlyDictionary<string, List<ProjectInfo>> projectsByName,
        ILookup<string, ProjectInfo> projectsByPath,
        string rootPath,
        List<ArchitectureProposalFinding> findings)
    {
        var changes = new Dictionary<(ProjectInfo From, ProjectInfo To), ArchitectureProposalReference>();
        foreach (var reference in references)
        {
            var operation = reference?.Operation?.Trim().ToLowerInvariant();
            if (reference is null || string.IsNullOrWhiteSpace(reference.From) ||
                string.IsNullOrWhiteSpace(reference.To) || operation is not ("add" or "remove"))
            {
                findings.Add(new("InvalidReference", "error", "A reference needs both endpoints and a valid operation.",
                    "Supply both project names and choose add or remove.", Reference: reference));
                continue;
            }

            var from = ResolveProject(reference.From, reference, projectsByName, projectsByPath, rootPath, findings);
            var to = ResolveProject(reference.To, reference, projectsByName, projectsByPath, rootPath, findings);
            if (from is null || to is null)
                continue;

            var normalized = new ArchitectureProposalReference(
                ProjectIdentity(from, projectsByName, rootPath), ProjectIdentity(to, projectsByName, rootPath), operation);

            if (ReferenceEquals(from, to))
            {
                findings.Add(new("SelfProjectReference", "error",
                    $"Project {normalized.From} cannot reference itself.",
                    "Remove the self-reference; a project cannot depend on itself.",
                    File: RelativeFile(from, rootPath), Reference: normalized));
                continue;
            }

            if (changes.TryAdd((from, to), normalized))
                continue;

            if (changes[(from, to)].Operation != operation)
            {
                findings.Add(new("ConflictingReferenceChanges", "error",
                    $"Reference {normalized.From} -> {normalized.To} is both added and removed.",
                    "Choose the intended final state for the reference.", Reference: normalized));
            }
        }

        return changes;
    }

    private static ProjectInfo? ResolveProject(
        string name,
        ArchitectureProposalReference reference,
        IReadOnlyDictionary<string, List<ProjectInfo>> projectsByName,
        ILookup<string, ProjectInfo> projectsByPath,
        string rootPath,
        List<ArchitectureProposalFinding> findings)
    {
        var project = name.Trim();
        var candidates = projectsByName.TryGetValue(project, out var names)
            ? names
            : projectsByPath[ProjectPath(project)].ToList();
        if (candidates.Count == 0)
        {
            findings.Add(new("UnknownProject", "error", $"Project {project} is outside the observed and proposed projects.",
                "Use a project name or root-relative project path from the scan, or declare it in the proposal.", Reference: reference));
            return null;
        }

        if (candidates.Count == 1)
            return candidates[0];

        findings.Add(new(ArchitectureChecker.AmbiguousProjectNameCode, "error",
            $"Multiple scanned projects match {project}.",
            "Use a root-relative project path from the candidate list to identify the project.",
            Candidates: CandidateFiles(candidates, rootPath), Reference: reference));
        return null;
    }

    private static string ProjectPath(string project)
    {
        var path = project.Replace('\\', '/');
        return path.StartsWith("./", StringComparison.Ordinal) ? path[2..] : path;
    }

    private static void CheckLayerRule(
        AnalysisResult result,
        ProjectInfo from,
        ProjectInfo to,
        ArchitectureProposalReference reference,
        bool layerModelAsserted,
        List<ArchitectureProposalFinding> findings)
    {
        var location = RelativeFile(from, result.RootPath);
        if (string.Equals(to.Layer, "Tests", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(from.Layer, "Tests", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new("ProductionReferenceToTests", "error",
                $"Production project {reference.From} would reference test project {reference.To}.",
                "Keep the dependency in the test project. Move shared production contracts to a production project.",
                File: location, Reference: reference));
        }

        var code = ProjectLayerChecks.GetLayerViolationCode(from.Layer, to.Layer);
        if (code is null)
            return;

        var bothLayersExplicit = string.IsNullOrEmpty(from.FilePath) && string.IsNullOrEmpty(to.FilePath);
        CheckSeverity? severity = layerModelAsserted || bothLayersExplicit ? CheckSeverity.Error : CheckSeverity.Warning;
        if (result.LayerRuleOverrides.TryGetValue(code, out var configured))
            severity = configured;
        if (severity is null)
            return;

        findings.Add(new(code, severity == CheckSeverity.Error ? "error" : "warning",
            $"{from.Layer} project {reference.From} would reference {to.Layer} project {reference.To}.",
            "Define a contract in the inner layer and implement it in the outer layer. Wire the implementation at the composition root.",
            File: location, Reference: reference));
    }

    private static void CheckForCycle(
        AnalysisResult result,
        ArchitectureProposalGraph graph,
        ProjectInfo from,
        ProjectInfo to,
        ArchitectureProposalReference reference,
        IReadOnlyDictionary<string, List<ProjectInfo>> projectsByName,
        HashSet<(ProjectInfo Project, string Include, string Condition)> conditionalWarned,
        List<ArchitectureProposalFinding> findings)
    {
        var (path, unresolved, visited) = graph.FindPathWithVisited(to, from);
        foreach (var visitedProject in visited)
            SurfaceConditionalReferences(visitedProject, reference, projectsByName, result.RootPath, conditionalWarned, findings);

        if (path is not null)
        {
            var cycle = path.Prepend(from)
                .Select(project => ProjectIdentity(project, projectsByName, result.RootPath))
                .ToArray();
            findings.Add(new(ArchitectureChecker.CircularProjectDependencyCode, "error",
                $"The proposed reference closes this cycle in the model. {string.Join(" -> ", cycle)}.",
                "Remove one dependency in this cycle or move the shared contract to a project both can reference.",
                File: RelativeFile(from, result.RootPath), DependencyPath: cycle, Reference: reference));
        }
        else if (unresolved is { } unknown)
        {
            findings.Add(new("UnresolvedProjectReference", "warning",
                $"The cycle check reached {unknown.Source.Name}, which has an unresolved reference to {unknown.Reference}.",
                "Inspect the referenced project and rerun from a root that includes it when possible.",
                File: RelativeFile(unknown.Source, result.RootPath), Reference: reference));
        }
    }

    private static void CheckTransitiveLayerViolations(
        AnalysisResult result,
        ArchitectureProposalGraph beforeGraph,
        ArchitectureProposalGraph afterGraph,
        ProjectInfo from,
        ProjectInfo to,
        ArchitectureProposalReference reference,
        bool layerModelAsserted,
        IReadOnlyDictionary<string, List<ProjectInfo>> projectsByName,
        HashSet<(ProjectInfo Inner, ProjectInfo Outer)> reportedTransitive,
        List<ArchitectureProposalFinding> findings)
    {
        var pathsToFrom = afterGraph.FindShortestPathsTo(from);
        foreach (var (inner, pathToFrom) in pathsToFrom
            .OrderBy(kv => kv.Key.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(kv => kv.Key.FilePath, StringComparer.Ordinal))
        {
            if (!string.Equals(inner.Layer, "Domain", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(inner.Layer, "Application", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var excluded = new HashSet<ProjectInfo>(pathToFrom);
            if (excluded.Contains(to))
                continue;

            var pathsFromTo = afterGraph.FindShortestPathsFromAvoiding(to, excluded);
            foreach (var (outer, pathFromTo) in pathsFromTo
                .OrderBy(kv => kv.Key.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(kv => kv.Key.FilePath, StringComparer.Ordinal))
            {
                if (pathToFrom.Count + pathFromTo.Count < 3)
                    continue;

                var code = ProjectLayerChecks.GetLayerViolationCode(inner.Layer, outer.Layer);
                if (code is null || beforeGraph.CanReach(inner, outer))
                    continue;

                var bothLayersExplicit = string.IsNullOrEmpty(inner.FilePath) && string.IsNullOrEmpty(outer.FilePath);
                CheckSeverity? severity = layerModelAsserted || bothLayersExplicit ? CheckSeverity.Error : CheckSeverity.Warning;
                if (result.LayerRuleOverrides.TryGetValue("TransitiveLayerViolation", out var transitiveConfigured))
                    severity = transitiveConfigured;
                else if (result.LayerRuleOverrides.TryGetValue(code, out var configured))
                    severity = configured;
                if (severity is null)
                    continue;

                if (!reportedTransitive.Add((inner, outer)))
                    continue;

                var dependencyPath = pathToFrom.Concat(pathFromTo)
                    .Select(project => ProjectIdentity(project, projectsByName, result.RootPath))
                    .ToArray();
                findings.Add(new("TransitiveLayerViolation",
                    severity == CheckSeverity.Error ? "error" : "warning",
                    $"{inner.Layer} project {ProjectIdentity(inner, projectsByName, result.RootPath)} would transitively reach {outer.Layer} project {ProjectIdentity(outer, projectsByName, result.RootPath)} via {string.Join(" -> ", dependencyPath)}.",
                    "Introduce a contract in the inner layer and invert the outer dependency at the composition root.",
                    File: RelativeFile(from, result.RootPath) ?? RelativeFile(inner, result.RootPath),
                    DependencyPath: dependencyPath,
                    Reference: reference));
            }
        }
    }

    private static void SurfaceConditionalReferences(
        ProjectInfo project,
        ArchitectureProposalReference? reference,
        IReadOnlyDictionary<string, List<ProjectInfo>> projectsByName,
        string rootPath,
        HashSet<(ProjectInfo Project, string Include, string Condition)> conditionalWarned,
        List<ArchitectureProposalFinding> findings)
    {
        foreach (var cond in project.ConditionalReferences)
        {
            if (!conditionalWarned.Add((project, cond.Include, cond.Condition)))
                continue;

            var targetDisplay = cond.ResolvedProjectName ?? cond.Include;
            findings.Add(new("ConditionalProjectReference", "warning",
                $"Project {ProjectIdentity(project, projectsByName, rootPath)} has a conditional reference to {targetDisplay} (Condition: {cond.Condition}).",
                "Verify how this MSBuild condition evaluates across build configurations because static proposal checks only traverse unconditional references.",
                File: RelativeFile(project, rootPath),
                Reference: reference));
        }
    }

    private static ArchitectureProposalProjectMetrics[] ComputeMetrics(
        IReadOnlyDictionary<string, List<ProjectInfo>> projectsByName,
        HashSet<ProjectInfo> proposedProjects,
        ArchitectureProposalGraph beforeGraph,
        ArchitectureProposalGraph afterGraph,
        string rootPath)
    {
        var metrics = new List<ArchitectureProposalProjectMetrics>();
        foreach (var project in projectsByName.Values.SelectMany(list => list).Distinct())
        {
            var caBefore = beforeGraph.AfferentCount(project);
            var ceBefore = beforeGraph.EfferentCount(project);
            var caAfter = afterGraph.AfferentCount(project);
            var ceAfter = afterGraph.EfferentCount(project);

            var changed = proposedProjects.Contains(project) ||
                          caBefore != caAfter ||
                          ceBefore != ceAfter ||
                          !beforeGraph.GetIncoming(project).ToHashSet().SetEquals(afterGraph.GetIncoming(project)) ||
                          !beforeGraph.GetOutgoing(project).ToHashSet().SetEquals(afterGraph.GetOutgoing(project));
            if (!changed)
                continue;

            metrics.Add(new ArchitectureProposalProjectMetrics(
                ProjectIdentity(project, projectsByName, rootPath),
                project.Layer,
                caBefore,
                caAfter,
                ceBefore,
                ceAfter,
                Instability(caBefore, ceBefore),
                Instability(caAfter, ceAfter)));
        }

        return metrics
            .OrderBy(m => m.Project, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Project, StringComparer.Ordinal)
            .ToArray();
    }

    private static double Instability(int afferent, int efferent)
    {
        var total = afferent + efferent;
        return total == 0 ? 0.0 : Math.Round((double)efferent / total, 3, MidpointRounding.AwayFromZero);
    }

    private static ArchitectureProposalBlastRadius ComputeBlastRadius(
        AnalysisResult result,
        IReadOnlyDictionary<string, List<ProjectInfo>> projectsByName,
        HashSet<ProjectInfo> proposedProjects,
        IReadOnlyList<(ProjectInfo From, ProjectInfo To)> added,
        IReadOnlyList<(ProjectInfo From, ProjectInfo To)> removed,
        ArchitectureProposalGraph beforeGraph)
    {
        var affectedProjects = new HashSet<ProjectInfo>(proposedProjects);
        foreach (var (from, to) in added)
        {
            affectedProjects.Add(from);
            affectedProjects.Add(to);
        }

        foreach (var (from, to) in removed)
        {
            affectedProjects.Add(from);
            affectedProjects.Add(to);
            foreach (var dependent in beforeGraph.GetTransitiveDependents(to))
                affectedProjects.Add(dependent);
        }

        var affectedProjectNames = affectedProjects
            .Select(project => ProjectIdentity(project, projectsByName, result.RootPath))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(name => name, StringComparer.Ordinal)
            .ToArray();

        bool BelongsToAffected(string? projectName, SourceLocation? location)
        {
            if (location is not null && !string.IsNullOrEmpty(result.RootPath))
            {
                var fullPath = Path.Combine(result.RootPath, location.File.Replace('/', Path.DirectorySeparatorChar));
                var owner = ProjectLocator.FindOwningProject(result.Projects, fullPath);
                if (owner is not null)
                    return affectedProjects.Contains(owner);
            }

            return !string.IsNullOrWhiteSpace(projectName) &&
                   affectedProjects.Any(project => string.Equals(project.Name, projectName, StringComparison.OrdinalIgnoreCase));
        }

        var impactedRequests = result.Requests
            .Where(r => BelongsToAffected(r.ProjectName, r.Location))
            .ToArray();
        var affectedHandlers = result.Handlers.Count(h => BelongsToAffected(h.ProjectName, h.Location));
        var affectedValidators = result.Validators.Count(v => BelongsToAffected(v.ProjectName, v.Location));
        var affectedEndpoints = result.Endpoints.Count(e => BelongsToAffected(e.ProjectName, e.Location));
        var affectedDbContexts = result.DbContexts.Count(d => BelongsToAffected(d.ProjectName, d.Location));
        var sampleRequests = impactedRequests
            .Select(r => r.FullName)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .Take(10)
            .ToArray();

        return new ArchitectureProposalBlastRadius(
            affectedProjectNames,
            impactedRequests.Length,
            affectedHandlers,
            affectedValidators,
            affectedEndpoints,
            affectedDbContexts,
            sampleRequests);
    }

    private static string[] CandidateFiles(IEnumerable<ProjectInfo> projects, string rootPath) => projects
        .Select(project => RelativeFile(project, rootPath))
        .OfType<string>()
        .Order(StringComparer.Ordinal)
        .ToArray();

    private static string ProjectIdentity(
        ProjectInfo project, IReadOnlyDictionary<string, List<ProjectInfo>> projectsByName, string rootPath)
        => projectsByName[project.Name].Count > 1 ? RelativeFile(project, rootPath) ?? project.Name : project.Name;

    private static string? RelativeFile(ProjectInfo project, string rootPath) =>
        SourceLocation.FromLine(project.FilePath, rootPath, 1, 1)?.File;
}
