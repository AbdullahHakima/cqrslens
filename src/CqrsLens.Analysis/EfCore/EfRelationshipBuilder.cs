namespace CqrsLens;

/// <summary>
/// Turns navigation properties and explicit configuration into ER relationships.
/// Ambiguous inverse navigations are reported instead of being paired by guesswork.
/// </summary>
internal static class EfRelationshipBuilder
{
    private const char KeySeparator = '\u001f';

    public static void Build(
        DbContextInfo context,
        IReadOnlyList<EfRelationshipConfiguration> configured,
        AnalysisResult result)
    {
        var entities = context.Entities
            .GroupBy(e => e.FullName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        if (entities.Count == 0) return;

        var navForeignKey = EfForeignKeyResolver.BuildExplicitForeignKeys(entities.Values);
        var handled = new HashSet<string>(StringComparer.Ordinal);
        var relationships = new List<EntityRelationshipInfo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var configuration in configured
                     .OrderBy(c => c.PrincipalEntity, StringComparer.Ordinal)
                     .ThenBy(c => c.DependentEntity, StringComparer.Ordinal)
                     .ThenBy(c => c.DependentNavigation, StringComparer.Ordinal))
        {
            if (!entities.TryGetValue(configuration.PrincipalEntity, out var principal) ||
                !entities.TryGetValue(configuration.DependentEntity, out var dependent))
                continue;

            var relation = EfRelationshipFactory.Create(
                principal,
                dependent,
                configuration.PrincipalNavigation,
                configuration.DependentNavigation,
                configuration.ForeignKey,
                configuration.IsRequired,
                configuration.Kind,
                configuration.ConfiguredBy ?? "fluent",
                navForeignKey);
            if (relation is null) continue;
            if (Add(relation, relationships, seen))
            {
                MarkHandled(handled, principal, configuration.PrincipalNavigation);
                MarkHandled(handled, dependent, configuration.DependentNavigation);
            }
        }

        foreach (var entity in entities.Values.OrderBy(e => e.FullName, StringComparer.Ordinal))
        {
            foreach (var navigation in entity.Properties
                         .Where(p => p.IsNavigation && p.TargetEntity is not null)
                         .OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                if (IsHandled(handled, entity, navigation.Name)) continue;
                if (!entities.TryGetValue(navigation.TargetEntity!, out var target))
                    continue;
                if (ReferenceEquals(entity, target) && IsHandled(handled, entity, navigation.Name))
                    continue;

                var resolution = EfNavigationRelationshipResolver.Resolve(
                    entity, navigation, target, navForeignKey, result, context);
                if (resolution.Ambiguous) continue;
                if (resolution.Relationship is null) continue;
                if (!Add(resolution.Relationship, relationships, seen)) continue;

                MarkHandled(handled, entity, navigation.Name);
                if (resolution.Inverse is not null)
                    MarkHandled(handled, target, resolution.Inverse.Name);
            }
        }

        context.Relationships.Clear();
        context.Relationships.AddRange(relationships
            .OrderBy(r => r.PrincipalEntity, StringComparer.Ordinal)
            .ThenBy(r => r.DependentEntity, StringComparer.Ordinal)
            .ThenBy(r => r.PrincipalNavigation, StringComparer.Ordinal)
            .ThenBy(r => r.DependentNavigation, StringComparer.Ordinal));
    }

    private static bool Add(
        EntityRelationshipInfo relationship,
        List<EntityRelationshipInfo> relationships,
        HashSet<string> seen)
    {
        var navs = new[]
        {
            relationship.PrincipalNavigation ?? "",
            relationship.DependentNavigation ?? "",
        };
        Array.Sort(navs, StringComparer.Ordinal);
        var key = string.Join(KeySeparator, new[]
        {
            relationship.PrincipalEntity,
            relationship.DependentEntity,
            relationship.Kind.ToString(),
            navs[0],
            navs[1],
            relationship.ForeignKey ?? "",
        });
        if (!seen.Add(key)) return false;
        relationships.Add(relationship);
        return true;
    }

    private static void MarkHandled(HashSet<string> handled, EntityInfo entity, string? navigation)
    {
        if (!string.IsNullOrEmpty(navigation))
            handled.Add(entity.FullName + KeySeparator + navigation);
    }

    private static bool IsHandled(HashSet<string> handled, EntityInfo entity, string navigation)
        => handled.Contains(entity.FullName + KeySeparator + navigation);
}
