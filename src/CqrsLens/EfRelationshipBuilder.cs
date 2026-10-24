namespace CqrsLens;

/// <summary>
/// A resolved Fluent API relationship. Text and Roslyn extraction both emit this
/// shape so relationship/cardinality rules live in one place.
/// </summary>
internal sealed record EfRelationshipConfiguration(
    string PrincipalEntity,
    string DependentEntity,
    string? PrincipalNavigation,
    string? DependentNavigation,
    string? ForeignKey,
    bool? IsRequired,
    EntityRelationshipKind Kind,
    string? ConfiguredBy = "fluent");

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

        var navForeignKey = BuildExplicitForeignKeys(entities.Values);
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

            var relation = Create(
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

                var inverse = FindInverse(entity, navigation, target, result, context.ProjectName);
                if (inverse.Ambiguous)
                {
                    RecordAmbiguity(result, context, navigation, inverse.Candidates);
                    continue;
                }

                EntityRelationshipInfo? relation;
                if (navigation.IsCollection)
                {
                    if (inverse.Property is { IsCollection: false } reference)
                    {
                        relation = Create(
                            entity, target, navigation.Name, reference.Name, null, null,
                            EntityRelationshipKind.OneToMany, "navigation", navForeignKey);
                    }
                    else if (inverse.Property is { IsCollection: true } otherCollection)
                    {
                        relation = Create(
                            entity, target, navigation.Name, otherCollection.Name, null, null,
                            EntityRelationshipKind.ManyToMany, "navigation", navForeignKey);
                    }
                    else
                    {
                        // EF Core treats a lone collection navigation as a
                        // unidirectional one-to-many; the foreign key lives on
                        // the dependent type (often as a shadow property).
                        relation = Create(
                            entity, target, navigation.Name, null, null, null,
                            EntityRelationshipKind.OneToMany, "navigation", navForeignKey);
                    }
                }
                else if (inverse.Property is { IsCollection: true } principalCollection)
                {
                    relation = Create(
                        target, entity, principalCollection.Name, navigation.Name, null, null,
                        EntityRelationshipKind.OneToMany, "navigation", navForeignKey);
                }
                else if (inverse.Property is { IsCollection: false } inverseReference)
                {
                    var dependentIsSelf = HasConventionalForeignKey(
                        entity, target, navigation.Name, navForeignKey);
                    var principal = dependentIsSelf ? target : entity;
                    var dependent = dependentIsSelf ? entity : target;
                    var principalNav = dependentIsSelf ? inverseReference.Name : navigation.Name;
                    var dependentNav = dependentIsSelf ? navigation.Name : inverseReference.Name;
                    relation = Create(
                        principal, dependent, principalNav, dependentNav, null, null,
                        EntityRelationshipKind.OneToOne, "navigation", navForeignKey);
                }
                else
                {
                    // A lone reference navigation is many-to-one from the entity
                    // that declares it.
                    relation = Create(
                        target, entity, null, navigation.Name, null, null,
                        EntityRelationshipKind.OneToMany, "navigation", navForeignKey);
                }

                if (relation is null) continue;
                if (!Add(relation, relationships, seen)) continue;
                MarkHandled(handled, entity, navigation.Name);
                if (inverse.Property is not null)
                    MarkHandled(handled, target, inverse.Property.Name);
            }
        }

        context.Relationships.Clear();
        context.Relationships.AddRange(relationships
            .OrderBy(r => r.PrincipalEntity, StringComparer.Ordinal)
            .ThenBy(r => r.DependentEntity, StringComparer.Ordinal)
            .ThenBy(r => r.PrincipalNavigation, StringComparer.Ordinal)
            .ThenBy(r => r.DependentNavigation, StringComparer.Ordinal));
    }

    private static EntityRelationshipInfo? Create(
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

        var foreignKeyProperty = FindProperty(dependent, foreignKey);
        if (foreignKey is null)
        {
            foreignKey = FindConventionalForeignKey(
                dependent, principal, dependentNavigation, navForeignKey);
            foreignKeyProperty = FindProperty(dependent, foreignKey);
        }

        if (foreignKeyProperty is not null)
            foreignKeyProperty.IsForeignKey = true;

        var required = configuredRequired
                       ?? foreignKeyProperty?.IsRequired
                       ?? FindNavigation(dependent, dependentNavigation)?.IsRequired
                       ?? false;

        var dependentNav = FindNavigation(dependent, dependentNavigation);
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

    private static IReadOnlyDictionary<string, Dictionary<string, string>> BuildExplicitForeignKeys(
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

    private static (EntityPropertyInfo? Property, bool Ambiguous, IReadOnlyList<string> Candidates)
        FindInverse(EntityInfo entity, EntityPropertyInfo navigation, EntityInfo target, AnalysisResult result, string? projectName)
    {
        var explicitInverse = EfAnnotations.AttributeArgument(navigation.Attributes, "InverseProperty");
        if (explicitInverse is not null)
        {
            var specified = FindNavigation(target, explicitInverse);
            if (specified is not null &&
                (string.Equals(specified.TargetEntity, entity.FullName, StringComparison.Ordinal) ||
                 ReferenceEquals(target, entity)))
                return (specified, false, Array.Empty<string>());

            result.RecordDiagnostic(new AnalysisDiagnostic(
                AnalysisResult.EfNavigationUnresolvedCode,
                AnalysisDiagnosticSeverity.Info,
                $"`{entity.Name}.{navigation.Name}` declares `[InverseProperty(\"{explicitInverse}\")]`, " +
                $"but `{target.Name}.{explicitInverse}` is not a matching navigation.",
                navigation.Location,
                projectName));
            return (null, false, Array.Empty<string>());
        }

        var candidates = target.Properties
            .Where(p => p.IsNavigation &&
                        string.Equals(p.TargetEntity, entity.FullName, StringComparison.Ordinal))
            .Where(p => !ReferenceEquals(entity, target) ||
                        !string.Equals(p.Name, navigation.Name, StringComparison.Ordinal))
            .ToList();

        if (candidates.Count == 0)
            return (null, false, Array.Empty<string>());

        var pointedBack = candidates
            .Where(p => string.Equals(
                EfAnnotations.AttributeArgument(p.Attributes, "InverseProperty"),
                navigation.Name,
                StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (pointedBack.Count == 1)
            return (pointedBack[0], false, Array.Empty<string>());
        if (candidates.Count == 1)
            return (candidates[0], false, Array.Empty<string>());

        return (null, true, candidates.Select(p => $"{target.Name}.{p.Name}").ToList());
    }

    private static void RecordAmbiguity(
        AnalysisResult result,
        DbContextInfo context,
        EntityPropertyInfo navigation,
        IReadOnlyList<string> candidates)
    {
        var list = string.Join(", ", candidates.OrderBy(c => c, StringComparer.Ordinal));
        result.RecordDiagnostic(new AnalysisDiagnostic(
            AnalysisResult.EfNavigationUnresolvedCode,
            AnalysisDiagnosticSeverity.Info,
            $"Navigation `{navigation.Name}` has more than one possible inverse ({list}); " +
            "no relationship was drawn. Add [InverseProperty] or Fluent API configuration.",
            navigation.Location,
            context.ProjectName));
    }

    private static string? FindConventionalForeignKey(
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

    private static bool HasConventionalForeignKey(
        EntityInfo dependent,
        EntityInfo principal,
        string? dependentNavigation,
        IReadOnlyDictionary<string, Dictionary<string, string>> navForeignKey)
        => FindConventionalForeignKey(dependent, principal, dependentNavigation, navForeignKey) is not null;

    private static EntityPropertyInfo? FindProperty(EntityInfo entity, string? name)
        => string.IsNullOrWhiteSpace(name)
            ? null
            : entity.Properties.FirstOrDefault(p =>
                string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    private static EntityPropertyInfo? FindNavigation(EntityInfo entity, string? name)
    {
        var property = FindProperty(entity, name);
        return property is { IsNavigation: true } ? property : null;
    }

    private static void MarkHandled(HashSet<string> handled, EntityInfo entity, string? navigation)
    {
        if (!string.IsNullOrEmpty(navigation))
            handled.Add(entity.FullName + KeySeparator + navigation);
    }

    private static bool IsHandled(HashSet<string> handled, EntityInfo entity, string navigation)
        => handled.Contains(entity.FullName + KeySeparator + navigation);
}
