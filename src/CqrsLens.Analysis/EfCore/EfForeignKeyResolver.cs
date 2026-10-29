namespace CqrsLens;

/// <summary>
/// Resolves foreign keys from <c>[ForeignKey]</c> annotations and EF Core naming
/// conventions for a dependent entity and its navigation.
/// </summary>
internal static class EfForeignKeyResolver
{
    internal static IReadOnlyDictionary<string, Dictionary<string, string>> BuildExplicitForeignKeys(
        IEnumerable<EntityInfo> entities)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var entity in entities)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var navigationNames = entity.Properties
                .Where(p => p.IsNavigation)
                .Select(p => p.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var property in entity.Properties)
            {
                var argument = EfAnnotations.AttributeArgument(property.Attributes, "ForeignKey");
                if (argument is null) continue;

                if (property.IsNavigation &&
                    entity.Properties.Any(p => !p.IsNavigation &&
                                               string.Equals(p.Name, argument, StringComparison.OrdinalIgnoreCase)))
                {
                    map[property.Name] = argument;
                }
                else if (!property.IsNavigation && navigationNames.Contains(argument))
                {
                    map[argument] = property.Name;
                }
            }

            if (map.Count > 0)
                result[entity.FullName] = map;
        }

        return result;
    }

    internal static string? FindConventionalForeignKey(
        EntityInfo dependent,
        EntityInfo principal,
        string? dependentNavigation,
        IReadOnlyDictionary<string, Dictionary<string, string>> navForeignKey)
    {
        if (dependentNavigation is not null &&
            navForeignKey.TryGetValue(dependent.FullName, out var map) &&
            map.TryGetValue(dependentNavigation, out var annotated))
            return annotated;

        var keyNames = principal.Properties
            .Where(p => p.IsKey)
            .Select(p => p.Name)
            .ToList();
        if (keyNames.Count == 0) keyNames.Add("Id");

        var candidates = new List<string>();
        if (!string.IsNullOrEmpty(dependentNavigation))
        {
            candidates.Add(dependentNavigation + "Id");
            foreach (var key in keyNames)
                candidates.Add(dependentNavigation + key);
        }
        candidates.Add(principal.Name + "Id");
        foreach (var key in keyNames)
            candidates.Add(principal.Name + key);

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var property = FindProperty(dependent, candidate);
            if (property is not null && !property.IsNavigation)
                return property.Name;
        }

        return null;
    }

    internal static bool HasConventionalForeignKey(
        EntityInfo dependent,
        EntityInfo principal,
        string? dependentNavigation,
        IReadOnlyDictionary<string, Dictionary<string, string>> navForeignKey)
        => FindConventionalForeignKey(dependent, principal, dependentNavigation, navForeignKey) is not null;

    internal static EntityPropertyInfo? FindProperty(EntityInfo entity, string? name)
        => string.IsNullOrWhiteSpace(name)
            ? null
            : entity.Properties.FirstOrDefault(p =>
                string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    internal static EntityPropertyInfo? FindNavigation(EntityInfo entity, string? name)
    {
        var property = FindProperty(entity, name);
        return property is { IsNavigation: true } ? property : null;
    }
}
