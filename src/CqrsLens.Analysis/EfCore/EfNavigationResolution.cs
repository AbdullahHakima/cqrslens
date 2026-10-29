namespace CqrsLens;

/// <summary>The outcome of inferring one relationship from a navigation property.</summary>
internal readonly record struct EfNavigationResolution(
    EntityRelationshipInfo? Relationship,
    EntityPropertyInfo? Inverse,
    bool Ambiguous)
{
    internal static EfNavigationResolution Skip { get; } = new(null, null, false);
    internal static EfNavigationResolution Unresolved { get; } = new(null, null, true);
}
