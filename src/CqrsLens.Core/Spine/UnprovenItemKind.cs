namespace CqrsLens;

/// <summary>
/// Categories of unproven, ambiguous, or unresolved facts recorded separately from
/// proven relations (F0 §5.4, §4.3).
/// </summary>
public enum UnprovenItemKind
{
    UnresolvedDispatch,
    AmbiguousResolution,
    AmbiguousLink,
    UnresolvedContract,
    UnresolvedEntity,
    UnresolvedNavigation,
    UnresolvedConfiguration,
    UnestablishedBehavior,
    ConventionRegistration,
    ServiceLocator,
    DynamicReflection,
    UnresolvedEventConsumer,
    UnimplementedInterface,
}
