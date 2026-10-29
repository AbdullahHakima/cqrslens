namespace CqrsLens;

/// <summary>
/// Creates a relationship record from resolved entities, resolving foreign key and
/// requiredness from explicit configuration, annotations and conventions.
/// </summary>
internal static class EfRelationshipFactory
{
    internal static EntityRelationshipInfo Create(
        EntityInfo principal,
        EntityInfo dependent,
        string? principalNavigation,
        string? dependentNavigation,
        string? configuredForeignKey,
        bool? configuredRequired,
        EntityRelationshipKind kind,
        string configuredBy,
        IReadOnlyDictionary<string, Dictionary<string, string>> navForeignKey)
    {
        if (kind == EntityRelationshipKind.ManyToMany)
        {
            return new EntityRelationshipInfo(
                principal.FullName,
                dependent.FullName,
                principalNavigation,
                dependentNavigation,
                null,
                false,
                kind,
                configuredBy);
        }

        var foreignKey = configuredForeignKey;
        if (foreignKey is null && dependentNavigation is not null)
        {
            var map = navForeignKey.TryGetValue(dependent.FullName, out var entry)
                ? entry
                : null;
            if (map is not null && map.TryGetValue(dependentNavigation, out var annotated))
                foreignKey = annotated;
        }

        var foreignKeyProperty = EfForeignKeyResolver.FindProperty(dependent, foreignKey);
        if (foreignKey is null)
        {
            foreignKey = EfForeignKeyResolver.FindConventionalForeignKey(
                dependent, principal, dependentNavigation, navForeignKey);
            foreignKeyProperty = EfForeignKeyResolver.FindProperty(dependent, foreignKey);
        }

        if (foreignKeyProperty is not null)
            foreignKeyProperty.IsForeignKey = true;

        var required = configuredRequired
                       ?? foreignKeyProperty?.IsRequired
                       ?? EfForeignKeyResolver.FindNavigation(dependent, dependentNavigation)?.IsRequired
                       ?? false;

        var dependentNav = EfForeignKeyResolver.FindNavigation(dependent, dependentNavigation);
        if (dependentNav is not null && configuredRequired is not null)
            dependentNav.IsRequired = configuredRequired.Value;

        return new EntityRelationshipInfo(
            principal.FullName,
            dependent.FullName,
            principalNavigation,
            dependentNavigation,
            foreignKey,
            required,
            kind,
            configuredBy);
    }
}
