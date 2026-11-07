namespace CqrsLens;

/// <summary>
/// Deterministic presentation order for pipeline behaviors by registration site.
/// <para>
/// Registered sites are ordered by their source file (ordinal) then their first
/// source line; within a site, behaviors are ordered by their 1-based registration
/// ordinal, then name, project and source. Behaviors with no resolvable registration
/// site follow the registered groups, ordered by name, project then source. This is a
/// presentation order only — it is neither an execution order nor an order claim
/// across sites.
/// </para>
/// </summary>
internal static class BehaviorOrdering
{
    internal sealed record SiteGroup(string Site, IReadOnlyList<BehaviorInfo> Behaviors);

    internal static IReadOnlyList<SiteGroup> Registered(IReadOnlyList<BehaviorInfo> behaviors)
        => behaviors
            .Where(b => b.RegistrationSite is not null)
            .GroupBy(b => b.RegistrationSite!, StringComparer.Ordinal)
            .Select(g => new SiteGroup(
                g.Key,
                g.OrderBy(b => b.RegistrationOrder ?? int.MaxValue)
                    .ThenBy(b => b.Name, StringComparer.Ordinal)
                    .ThenBy(b => b.ProjectName, StringComparer.Ordinal)
                    .ThenBy(b => b.Location?.File, StringComparer.Ordinal)
                    .ThenBy(b => b.Location?.Line ?? 0)
                    .ToList()))
            .OrderBy(g => g.Behaviors.Min(b => b.Location?.File) ?? g.Site, StringComparer.Ordinal)
            .ThenBy(g => g.Behaviors.Min(b => b.Location?.Line ?? int.MaxValue))
            .ThenBy(g => g.Site, StringComparer.Ordinal)
            .ToList();

    internal static IReadOnlyList<BehaviorInfo> Unregistered(IReadOnlyList<BehaviorInfo> behaviors)
        => behaviors
            .Where(b => b.RegistrationSite is null)
            .OrderBy(b => b.Name, StringComparer.Ordinal)
            .ThenBy(b => b.ProjectName, StringComparer.Ordinal)
            .ThenBy(b => b.Location?.File, StringComparer.Ordinal)
            .ThenBy(b => b.Location?.Line ?? 0)
            .ToList();

    /// <summary>All behaviors in presentation order: registered groups, then unregistered.</summary>
    internal static IReadOnlyList<BehaviorInfo> Flattened(IReadOnlyList<BehaviorInfo> behaviors)
        => Registered(behaviors)
            .SelectMany(g => g.Behaviors)
            .Concat(Unregistered(behaviors))
            .ToList();
}
