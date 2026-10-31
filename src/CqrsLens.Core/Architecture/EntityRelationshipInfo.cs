namespace CqrsLens;

/// <summary>
/// A relationship between two entities. For one-to-many, <see cref="PrincipalEntity"/>
/// is the "one" side and <see cref="DependentEntity"/> is the "many" side. For
/// one-to-one the principal/dependent orientation comes from the configured or
/// conventional foreign key; many-to-many has no meaningful dependent side.
/// </summary>
public sealed record EntityRelationshipInfo(
    string PrincipalEntity,
    string DependentEntity,
    string? PrincipalNavigation,
    string? DependentNavigation,
    string? ForeignKey,
    bool IsRequired,
    EntityRelationshipKind Kind,
    string? ConfiguredBy = null);
