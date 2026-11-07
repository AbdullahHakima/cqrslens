namespace CqrsLens;

/// <summary>
/// Builds the combined, solution-level data view: every DbContext with its entities,
/// mapped DbSets and relationships in one place. The per-context entity set is the
/// same set the context's ER diagram uses, and every context is kept — including
/// contexts with no entities discovered. Rendering only; no new detection.
/// </summary>
internal static class DataModelView
{
    /// <summary>One DbContext and its (ordered) entities, DbSets and relationships.</summary>
    internal sealed record ContextView(
        string Name,
        string DisplayName,
        string? Project,
        string? Namespace,
        IReadOnlyList<EntityInfo> Entities,
        IReadOnlyList<DbSetInfo> DbSets,
        IReadOnlyList<EntityRelationshipInfo> Relationships)
    {
        internal int EntityCount => Entities.Count;
        internal int RelationshipCount => Relationships.Count;
    }

    internal sealed record Summary(
        IReadOnlyList<ContextView> Contexts,
        int ContextCount,
        int ContextsWithEntities,
        int ContextsWithoutEntities,
        int EntityCount,
        int RelationshipCount,
        int DbSetCount);

    internal static Summary Build(AnalysisResult result)
    {
        var ordered = result.DbContexts
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Namespace, StringComparer.Ordinal)
            .ToList();
        var nameCounts = ordered
            .GroupBy(c => c.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var contexts = ordered.Select(context => BuildContext(context, nameCounts)).ToList();

        return new Summary(
            contexts,
            contexts.Count,
            contexts.Count(c => c.EntityCount > 0),
            contexts.Count(c => c.EntityCount == 0),
            contexts.Sum(c => c.EntityCount),
            contexts.Sum(c => c.RelationshipCount),
            contexts.Sum(c => c.DbSets.Count));
    }

    private static ContextView BuildContext(DbContextInfo context, Dictionary<string, int> nameCounts)
    {
        var entities = context.Entities
            .OrderBy(e => e.FullName, StringComparer.Ordinal)
            .ToList();
        var entityFullNames = new HashSet<string>(entities.Select(e => e.FullName), StringComparer.Ordinal);

        var relationships = context.Relationships
            .Where(r => entityFullNames.Contains(r.PrincipalEntity) &&
                        entityFullNames.Contains(r.DependentEntity))
            .OrderBy(r => r.PrincipalEntity, StringComparer.Ordinal)
            .ThenBy(r => r.DependentEntity, StringComparer.Ordinal)
            .ThenBy(r => r.PrincipalNavigation, StringComparer.Ordinal)
            .ThenBy(r => r.DependentNavigation, StringComparer.Ordinal)
            .ToList();

        var dbSets = context.DbSets
            .OrderBy(s => s.Property, StringComparer.Ordinal)
            .ThenBy(s => s.Entity, StringComparer.Ordinal)
            .ToList();

        var display = nameCounts[context.Name] > 1 && !string.IsNullOrEmpty(context.Namespace)
            ? $"{context.Namespace}.{context.Name}"
            : context.Name;

        return new ContextView(
            context.Name, display, context.ProjectName, context.Namespace, entities, dbSets, relationships);
    }

    /// <summary>Entity label: the full name when two entities in the context share a name, else the name.</summary>
    internal static string EntityLabel(ContextView context, EntityInfo entity)
    {
        var collisions = context.Entities.Count(e => string.Equals(e.Name, entity.Name, StringComparison.Ordinal));
        return collisions > 1 ? entity.FullName : entity.Name;
    }

    internal static string KeyLabel(EntityInfo entity)
    {
        var keys = entity.Properties
            .Where(p => p.IsKey)
            .Select(p => p.Name)
            .ToList();
        return keys.Count == 0 ? "—" : string.Join(", ", keys);
    }

    internal static string DbSetLabel(ContextView context, EntityInfo entity)
    {
        var sets = context.DbSets
            .Where(s => string.Equals(s.EntityFullName, entity.FullName, StringComparison.Ordinal) ||
                        string.Equals(s.Entity, entity.Name, StringComparison.Ordinal))
            .Select(s => s.Property)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return sets.Count == 0 ? "—" : string.Join(", ", sets);
    }

    internal static string NavigationLabel(EntityRelationshipInfo relationship)
    {
        var principal = relationship.PrincipalNavigation;
        var dependent = relationship.DependentNavigation;
        if (!string.IsNullOrEmpty(principal) && !string.IsNullOrEmpty(dependent))
        {
            return string.Equals(principal, dependent, StringComparison.Ordinal)
                ? principal
                : $"{principal} / {dependent}";
        }

        if (!string.IsNullOrEmpty(principal)) return principal;
        if (!string.IsNullOrEmpty(dependent)) return dependent;
        return relationship.ForeignKey ?? "—";
    }

    internal static string KindLabel(EntityRelationshipKind kind) => kind switch
    {
        EntityRelationshipKind.OneToOne => "one-to-one",
        EntityRelationshipKind.OneToMany => "one-to-many",
        EntityRelationshipKind.ManyToMany => "many-to-many",
        _ => "relationship",
    };
}
