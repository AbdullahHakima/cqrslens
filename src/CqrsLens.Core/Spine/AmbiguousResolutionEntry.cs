namespace CqrsLens;

/// <summary>
/// An interface whose candidate implementations could not be narrowed to a single
/// resolved implementation (fail-closed, never emitted as a <see cref="RelationKind.ResolvesTo"/> edge) (F0 §5.4).
/// </summary>
public sealed record AmbiguousResolutionEntry(
    string InterfaceSignature,
    string InterfaceName,
    IReadOnlyList<ServiceCandidate> Candidates,
    SourceLocation? InterfaceLocation = null);
