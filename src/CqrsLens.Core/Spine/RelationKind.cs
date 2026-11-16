namespace CqrsLens;

/// <summary>
/// Directed, statically justified relation kinds in the F1 architecture spine (F0 §4).
/// Future F2 relations (e.g. Exposes) are not admitted in F1.
/// </summary>
public enum RelationKind
{
    References,
    Serves,
    Validates,
    Wraps,
    Dispatches,
    Publishes,
    HandlesNotification,
    UsesData,
    UsesDataResolved,
    ResolvesTo,
    Calls,
    TouchesData,
    Relates,
    Exposes,
}
