namespace CqrsLens;

/// <summary>
/// A discovered behavior whose <see cref="RelationKind.Wraps"/> applicability to specific
/// requests could not be established under F0 §4.3.
/// </summary>
public sealed record UnestablishedBehaviorEntry(
    ComponentId? BehaviorComponentId,
    string BehaviorName,
    UnestablishedBehaviorReason Reason,
    string? RegistrationSite = null,
    string? ProjectName = null,
    SourceLocation? Location = null);
