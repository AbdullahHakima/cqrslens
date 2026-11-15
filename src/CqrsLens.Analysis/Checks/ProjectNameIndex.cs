namespace CqrsLens;

/// <summary>
/// Groups analyzed projects by name and reports duplicate names, which make layer
/// references ambiguous.
/// </summary>
internal static class ProjectNameIndex
{
    internal static Dictionary<string, List<ProjectInfo>> Build(AnalysisResult result)
        => result.Projects
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

    internal static void ReportAmbiguousNames(
        AnalysisResult result,
        IReadOnlyDictionary<string, List<ProjectInfo>> projectsByName,
        List<CheckFinding> findings)
    {
        // The warning exists because layer checks skip references to a duplicated
        // project name. Only a reference from a project whose layer is actually
        // checked (Domain or Application) creates that blind spot; duplicated names
        // that no such project references are unrelated noise on large repos.
        var referencedByCheckedLayer = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rel in result.Spine.UnindexedRelations)
        {
            if (rel.Kind != RelationKind.References || rel.From.ProjectId is null)
                continue;
            if (!result.Spine.TryGetProject(rel.From.ProjectId, out var fromProject))
                continue;
            if (!string.Equals(fromProject!.Layer, "Domain", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(fromProject.Layer, "Application", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            referencedByCheckedLayer.Add(
                SpineQueries.FormatProjectReferenceTarget(result.IdentityResolver, rel.To));
        }

        foreach (var (name, projects) in projectsByName.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (projects.Count <= 1 || !referencedByCheckedLayer.Contains(name))
                continue;

            findings.Add(new CheckFinding(
                CheckSeverity.Warning,
                ArchitectureChecker.AmbiguousProjectNameCode,
                $"Project name `{name}` is used by {projects.Count} projects; layer checks skip references to it.",
                Target: name,
                Location: SourceLocation.FromLine(projects[0].FilePath, result.RootPath, 1, 1),
                ProjectName: name));
        }
    }
}
