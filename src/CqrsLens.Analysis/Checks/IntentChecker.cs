using System.Runtime.CompilerServices;

namespace CqrsLens;

/// <summary>
/// Evaluates declared human intent (<see cref="IntentSpecification"/>) against the
/// settled <see cref="ArchitectureSpine"/>: <c>C-BOUNDARY</c>, <c>C-SURFACE</c>, and
/// stale module selectors (<c>C-INTENT-STALE</c>). Ownership is never checked (R4).
/// </summary>
internal static class IntentChecker
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Run(AnalysisResult result, List<CheckFinding> findings)
    {
        var intent = result.Intent;
        if (intent is null)
            return;

        var spine = result.Spine;
        var projectsById = new Dictionary<ProjectId, ProjectNode>(spine.Projects.Count);
        foreach (var proj in spine.Projects)
            projectsById[proj.Id] = proj;

        var moduleSets = new Dictionary<string, ModuleMembership>(StringComparer.Ordinal);
        foreach (var module in intent.Modules.OrderBy(m => m.Name, StringComparer.Ordinal))
        {
            var matchedProjects = new HashSet<ProjectId>();
            foreach (var selector in module.Projects)
            {
                var found = false;
                foreach (var project in spine.Projects)
                {
                    if (IntentModuleMatcher.MatchesProjectSelector(selector, project))
                    {
                        matchedProjects.Add(project.Id);
                        found = true;
                    }
                }

                if (!found)
                {
                    findings.Add(new CheckFinding(
                        CheckSeverity.Warning,
                        ArchitectureChecker.IntentStaleCode,
                        $"Stale intent (C-INTENT-STALE): module '{module.Name}' references missing project '{selector}'.",
                        Target: $"{module.Name}:{selector}"));
                }
            }

            var matchedComponents = new HashSet<ComponentId>();
            foreach (var component in spine.Components)
            {
                if (matchedProjects.Contains(component.Project))
                    matchedComponents.Add(component.Id);
            }

            foreach (var selector in module.Components)
            {
                var found = false;
                foreach (var component in spine.Components)
                {
                    if (IntentModuleMatcher.MatchesComponentSelector(selector, component))
                    {
                        matchedComponents.Add(component.Id);
                        found = true;
                    }
                }

                if (!found)
                {
                    findings.Add(new CheckFinding(
                        CheckSeverity.Warning,
                        ArchitectureChecker.IntentStaleCode,
                        $"Stale intent (C-INTENT-STALE): module '{module.Name}' references missing component '{selector}'.",
                        Target: $"{module.Name}:{selector}"));
                }
            }

            moduleSets[module.Name] = new ModuleMembership(module, matchedProjects, matchedComponents);
        }

        if (intent.Boundaries.Count > 0)
            CheckBoundaries(intent.Boundaries, moduleSets, spine, projectsById, findings);

        if (intent.PublicSurface is not null)
            CheckPublicSurface(intent.PublicSurface, spine, projectsById, findings);
    }

    private static void CheckBoundaries(
        IReadOnlyList<IntentBoundaryRule> boundaries,
        IReadOnlyDictionary<string, ModuleMembership> moduleSets,
        ArchitectureSpine spine,
        IReadOnlyDictionary<ProjectId, ProjectNode> projectsById,
        List<CheckFinding> findings)
    {
        var calls = spine.GetRelations(RelationKind.Calls);
        if (calls.Count == 0)
            return;

        var orderedBoundaries = boundaries
            .OrderBy(b => b.FromModule, StringComparer.Ordinal)
            .ThenBy(b => b.ToModule, StringComparer.Ordinal)
            .ThenByDescending(b => b.Severity)
            .ToList();

        foreach (var boundary in orderedBoundaries)
        {
            if (!moduleSets.TryGetValue(boundary.FromModule, out var fromMod) ||
                !moduleSets.TryGetValue(boundary.ToModule, out var toMod))
            {
                continue;
            }

            foreach (var rel in calls)
            {
                if (!MatchesNode(rel.From, fromMod, spine, projectsById))
                    continue;
                if (!MatchesNode(rel.To, toMod, spine, projectsById))
                    continue;

                var evidenceSites = new List<string>();
                SourceLocation? primaryLocation = null;
                var seenSites = new HashSet<string>(StringComparer.Ordinal);
                foreach (var occ in rel.Occurrences)
                {
                    if (occ.Location is null)
                        continue;

                    primaryLocation ??= occ.Location;
                    var site = $"{occ.Location.File}:{occ.Location.Line}";
                    if (seenSites.Add(site))
                        evidenceSites.Add(site);
                }

                var evidenceText = evidenceSites.Count == 0
                    ? "no source location"
                    : evidenceSites.Count <= 3
                        ? string.Join(", ", evidenceSites)
                        : $"{string.Join(", ", evidenceSites.Take(3))} (+{evidenceSites.Count - 3} more)";

                var sourceProject = rel.From.ProjectId is not null &&
                                    projectsById.TryGetValue(rel.From.ProjectId, out var proj)
                    ? proj.Name
                    : null;
                var targetId = rel.From.ComponentId?.Value ?? rel.From.Name;

                findings.Add(new CheckFinding(
                    boundary.Severity,
                    ArchitectureChecker.IntentBoundaryCode,
                    $"Intent boundary violation: module '{boundary.FromModule}' must not call '{boundary.ToModule}' — " +
                    $"'{rel.From.Name}' calls '{rel.To.Name}' at {evidenceText}.",
                    Target: targetId,
                    Location: primaryLocation,
                    ProjectName: sourceProject));
            }
        }
    }

    private static bool MatchesNode(
        NodeReference node,
        ModuleMembership membership,
        ArchitectureSpine spine,
        IReadOnlyDictionary<ProjectId, ProjectNode> projectsById)
    {
        if (node.ComponentId is not null)
            return membership.Components.Contains(node.ComponentId);

        if (node.SymbolId is not null)
        {
            var symCompId = ComponentId.FromSymbol(node.SymbolId);
            if (membership.Components.Contains(symCompId))
                return true;

            if (membership.Projects.Contains(node.SymbolId.Project))
                return true;

            foreach (var selector in membership.Definition.Components)
            {
                if (IntentModuleMatcher.MatchesSymbolSelector(selector, node.SymbolId))
                    return true;
            }

            return false;
        }

        if (node.ProjectId is not null)
            return membership.Projects.Contains(node.ProjectId);

        return IntentModuleMatcher.NodeBelongsToModule(node, membership.Definition, spine, projectsById);
    }

    private static void CheckPublicSurface(
        IntentPublicSurface surface,
        ArchitectureSpine spine,
        IReadOnlyDictionary<ProjectId, ProjectNode> projectsById,
        List<CheckFinding> findings)
    {
        if (surface.Endpoints is not null)
        {
            var matched = new bool[surface.Endpoints.Count];
            foreach (var endpoint in spine.Endpoints)
            {
                var anyMatch = false;
                for (var i = 0; i < surface.Endpoints.Count; i++)
                {
                    if (MatchesPublicEndpoint(surface.Endpoints[i], endpoint))
                    {
                        matched[i] = true;
                        anyMatch = true;
                    }
                }

                if (!anyMatch)
                {
                    var projName = projectsById.TryGetValue(endpoint.Project, out var proj) ? proj.Name : null;
                    findings.Add(new CheckFinding(
                        surface.Severity,
                        ArchitectureChecker.IntentSurfaceCode,
                        $"Public surface mismatch (C-SURFACE): endpoint '{endpoint.DisplayLabel}' ({endpoint.Id}) is not in the declared public surface.",
                        Target: endpoint.Id.ToString(),
                        Location: endpoint.Location,
                        ProjectName: projName));
                }
            }

            for (var i = 0; i < surface.Endpoints.Count; i++)
            {
                if (!matched[i])
                {
                    findings.Add(new CheckFinding(
                        surface.Severity,
                        ArchitectureChecker.IntentSurfaceCode,
                        $"Stale public surface intent (C-SURFACE): declared public endpoint '{surface.Endpoints[i]}' is absent from the model.",
                        Target: surface.Endpoints[i]));
                }
            }
        }

        if (surface.Components is not null)
        {
            var matched = new bool[surface.Components.Count];
            foreach (var component in spine.Components)
            {
                var anyMatch = false;
                for (var i = 0; i < surface.Components.Count; i++)
                {
                    if (MatchesPublicComponent(surface.Components[i], component))
                    {
                        matched[i] = true;
                        anyMatch = true;
                    }
                }

                if (!anyMatch)
                {
                    var projName = projectsById.TryGetValue(component.Project, out var proj) ? proj.Name : null;
                    findings.Add(new CheckFinding(
                        surface.Severity,
                        ArchitectureChecker.IntentSurfaceCode,
                        $"Public surface mismatch (C-SURFACE): component '{component.Id}' is not in the declared public surface.",
                        Target: component.Id.Value,
                        Location: component.Location,
                        ProjectName: projName));
                }
            }

            for (var i = 0; i < surface.Components.Count; i++)
            {
                if (!matched[i])
                {
                    findings.Add(new CheckFinding(
                        surface.Severity,
                        ArchitectureChecker.IntentSurfaceCode,
                        $"Stale public surface intent (C-SURFACE): declared public component '{surface.Components[i]}' is absent from the model.",
                        Target: surface.Components[i]));
                }
            }
        }
    }

    private static bool MatchesPublicEndpoint(string selector, EndpointNode endpoint)
    {
        if (string.Equals(endpoint.Id.ToString(), selector, StringComparison.Ordinal))
            return true;

        if (string.Equals(endpoint.Name, selector, StringComparison.Ordinal))
            return true;

        if (!string.IsNullOrEmpty(endpoint.Route) &&
            string.Equals(endpoint.Route, selector, StringComparison.Ordinal))
        {
            return true;
        }

        var space = selector.IndexOf(' ');
        if (space > 0 && space + 1 < selector.Length)
        {
            var verb = selector[..space];
            var rest = selector[(space + 1)..].TrimStart();
            if (string.Equals(verb, endpoint.HttpMethod, StringComparison.OrdinalIgnoreCase))
            {
                if ((!string.IsNullOrEmpty(endpoint.Route) && string.Equals(rest, endpoint.Route, StringComparison.Ordinal)) ||
                    string.Equals(rest, endpoint.Name, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return string.Equals(endpoint.DisplayLabel, selector, StringComparison.Ordinal);
    }

    private static bool MatchesPublicComponent(string selector, ComponentNode component)
        => string.Equals(component.Id.Value, selector, StringComparison.Ordinal) ||
           string.Equals(component.Symbol.QualifiedName, selector, StringComparison.Ordinal) ||
           string.Equals(component.Symbol.FullName, selector, StringComparison.Ordinal) ||
           string.Equals(component.Name, selector, StringComparison.Ordinal);

    private sealed record ModuleMembership(
        IntentModuleDefinition Definition,
        HashSet<ProjectId> Projects,
        HashSet<ComponentId> Components);
}
