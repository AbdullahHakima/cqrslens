namespace CqrsLens;

/// <summary>
/// Infers an ER relationship from a navigation property and its inverse navigation.
/// Ambiguous inverses are reported instead of being paired by guesswork.
/// </summary>
internal static class EfNavigationRelationshipResolver
{
    internal static EfNavigationResolution Resolve(
        EntityInfo entity,
        EntityPropertyInfo navigation,
        EntityInfo target,
        IReadOnlyDictionary<string, Dictionary<string, string>> navForeignKey,
        AnalysisResult result,
        DbContextInfo context)
    {
        var inverse = FindInverse(entity, navigation, target, result, context.ProjectName);
        if (inverse.Ambiguous)
        {
            RecordAmbiguity(result, context, navigation, inverse.Candidates);
            return EfNavigationResolution.Unresolved;
        }

        EntityRelationshipInfo? relation;
        if (navigation.IsCollection)
        {
            if (inverse.Property is { IsCollection: false } reference)
            {
                relation = EfRelationshipFactory.Create(
                    entity, target, navigation.Name, reference.Name, null, null,
                    EntityRelationshipKind.OneToMany, "navigation", navForeignKey);
            }
            else if (inverse.Property is { IsCollection: true } otherCollection)
            {
                relation = EfRelationshipFactory.Create(
                    entity, target, navigation.Name, otherCollection.Name, null, null,
                    EntityRelationshipKind.ManyToMany, "navigation", navForeignKey);
            }
            else
            {
                // EF Core treats a lone collection navigation as a
                // unidirectional one-to-many; the foreign key lives on
                // the dependent type (often as a shadow property).
                relation = EfRelationshipFactory.Create(
                    entity, target, navigation.Name, null, null, null,
                    EntityRelationshipKind.OneToMany, "navigation", navForeignKey);
            }
        }
        else if (inverse.Property is { IsCollection: true } principalCollection)
        {
            relation = EfRelationshipFactory.Create(
                target, entity, principalCollection.Name, navigation.Name, null, null,
                EntityRelationshipKind.OneToMany, "navigation", navForeignKey);
        }
        else if (inverse.Property is { IsCollection: false } inverseReference)
        {
            var dependentIsSelf = EfForeignKeyResolver.HasConventionalForeignKey(
                entity, target, navigation.Name, navForeignKey);
            var principal = dependentIsSelf ? target : entity;
            var dependent = dependentIsSelf ? entity : target;
            var principalNav = dependentIsSelf ? inverseReference.Name : navigation.Name;
            var dependentNav = dependentIsSelf ? navigation.Name : inverseReference.Name;
            relation = EfRelationshipFactory.Create(
                principal, dependent, principalNav, dependentNav, null, null,
                EntityRelationshipKind.OneToOne, "navigation", navForeignKey);
        }
        else
        {
            // A lone reference navigation is many-to-one from the entity
            // that declares it.
            relation = EfRelationshipFactory.Create(
                target, entity, null, navigation.Name, null, null,
                EntityRelationshipKind.OneToMany, "navigation", navForeignKey);
        }

        return relation is null
            ? EfNavigationResolution.Skip
            : new EfNavigationResolution(relation, inverse.Property, false);
    }

    private static (EntityPropertyInfo? Property, bool Ambiguous, IReadOnlyList<string> Candidates)
        FindInverse(
            EntityInfo entity,
            EntityPropertyInfo navigation,
            EntityInfo target,
            AnalysisResult result,
            string? projectName)
    {
        var explicitInverse = EfAnnotations.AttributeArgument(navigation.Attributes, "InverseProperty");
        if (explicitInverse is not null)
        {
            var specified = EfForeignKeyResolver.FindNavigation(target, explicitInverse);
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
}
