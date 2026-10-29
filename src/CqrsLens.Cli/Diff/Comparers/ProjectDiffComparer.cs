using static CqrsLens.JsonReportGenerator;

namespace CqrsLens;

/// <summary>Matches projects by name and reports layer, reference and package changes.</summary>
internal static class ProjectDiffComparer
{
    public static ProjectDiffSection Compare(
        IReadOnlyList<JsonProject> baseProjects,
        IReadOnlyList<JsonProject> headProjects)
    {
        var section = new ProjectDiffSection();
        var baseByName = baseProjects
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var headByName = headProjects
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var head in headProjects.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!baseByName.TryGetValue(head.Name, out var baseline))
            {
                section.Added.Add(head);
                continue;
            }

            var layerChanged = !string.Equals(baseline.Layer, head.Layer, StringComparison.OrdinalIgnoreCase);
            var addedReferences = Difference(head.References, baseline.References);
            var removedReferences = Difference(baseline.References, head.References);
            var addedPackages = Difference(head.Packages, baseline.Packages);
            var removedPackages = Difference(baseline.Packages, head.Packages);

            if (!layerChanged &&
                addedReferences.Count == 0 &&
                removedReferences.Count == 0 &&
                addedPackages.Count == 0 &&
                removedPackages.Count == 0)
            {
                continue;
            }

            section.Modified.Add(new ModifiedProjectDiff
            {
                Name = head.Name,
                OldLayer = layerChanged ? baseline.Layer : null,
                NewLayer = layerChanged ? head.Layer : null,
                AddedReferences = addedReferences,
                RemovedReferences = removedReferences,
                AddedPackages = addedPackages,
                RemovedPackages = removedPackages,
            });
        }

        foreach (var baseline in baseProjects.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!headByName.ContainsKey(baseline.Name))
                section.Removed.Add(baseline);
        }

        return section;
    }

    private static List<string> Difference(IReadOnlyList<string> left, IReadOnlyList<string> right)
        => left.Except(right, StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
