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
