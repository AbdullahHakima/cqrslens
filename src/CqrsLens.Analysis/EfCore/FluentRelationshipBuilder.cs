namespace CqrsLens;

/// <summary>
/// Turns parsed fluent navigation calls into a principal/dependent relationship with
/// cardinality, matching the conventions EF Core applies.
/// </summary>
internal static class FluentRelationshipBuilder
{
    internal static EfRelationshipConfiguration Build(
        string entity,
        string target,
        string? firstNavigation,
        bool? firstIsCollection,
        string? inverseNavigation,
        bool? inverseIsCollection,
        string? foreignKey,
        string? foreignKeyTarget,
        bool? required,
        CSharpScanner.ClassDecl? entityDecl,
        TypeHierarchy hierarchy)
    {
        var hasInverse = inverseIsCollection is not null || inverseNavigation is not null;
        var firstCollection = firstIsCollection == true;
        EntityRelationshipKind kind;
        string principal;
        string dependent;
        string? principalNavigation;
        string? dependentNavigation;

        if (hasInverse)
        {
            if (firstCollection && inverseIsCollection == true)
            {
                kind = EntityRelationshipKind.ManyToMany;
                principal = entity;
                dependent = target;
                principalNavigation = firstNavigation;
                dependentNavigation = inverseNavigation;
            }
            else if (firstCollection && inverseIsCollection == false)
            {
                kind = EntityRelationshipKind.OneToMany;
                principal = entity;
                dependent = target;
                principalNavigation = firstNavigation;
                dependentNavigation = inverseNavigation;
            }
            else if (!firstCollection && inverseIsCollection == true)
            {
                kind = EntityRelationshipKind.OneToMany;
                principal = target;
                dependent = entity;
                principalNavigation = inverseNavigation;
                dependentNavigation = firstNavigation;
            }
            else
            {
                kind = EntityRelationshipKind.OneToOne;
                var foreignKeyOnEntity = foreignKeyTarget == entity ||
                                         (foreignKey is not null &&
                                          FluentTypeResolver.FindProperty(entityDecl, foreignKey, hierarchy) is not null);
                if (foreignKeyOnEntity)
                {
                    principal = target;
                    dependent = entity;
                    principalNavigation = inverseNavigation;
                    dependentNavigation = firstNavigation;
                }
                else
                {
                    principal = entity;
                    dependent = target;
                    principalNavigation = firstNavigation;
                    dependentNavigation = inverseNavigation;
                }
            }
        }
        else if (firstCollection)
        {
            kind = EntityRelationshipKind.OneToMany;
            principal = entity;
            dependent = target;
            principalNavigation = firstNavigation;
            dependentNavigation = null;
        }
        else
        {
            kind = EntityRelationshipKind.OneToMany;
            principal = target;
            dependent = entity;
            principalNavigation = null;
            dependentNavigation = firstNavigation;
        }

        return new EfRelationshipConfiguration(
            principal,
            dependent,
            principalNavigation,
            dependentNavigation,
            foreignKey,
            required,
            kind,
            "fluent");
    }

    /// <summary>Identity used to keep one fluent relationship per principal/dependent/navigation shape.</summary>
    internal static string DedupeKey(EfRelationshipConfiguration relationship)
        => string.Join("\u001f", relationship.PrincipalEntity, relationship.DependentEntity,
            relationship.PrincipalNavigation ?? "", relationship.DependentNavigation ?? "",
            relationship.ForeignKey ?? "", relationship.Kind.ToString());
}
