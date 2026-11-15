namespace CqrsLens;

/// <summary>
/// Kind of addressable node in the architecture spine: structural containers, components,
/// and typed references (F0 §3).
/// </summary>
public enum NodeReferenceKind
{
    Project,
    Layer,
    Component,
    Endpoint,
    Interface,
    Method,
    Member,
    Type,
    External,
    Unresolved,
}
