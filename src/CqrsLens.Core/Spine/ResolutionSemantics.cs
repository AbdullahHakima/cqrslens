namespace CqrsLens;

/// <summary>
/// Tightened resolution semantics carried in <see cref="RelationKind.ResolvesTo"/> evidence (F0 §4.4).
/// </summary>
public enum ResolutionSemantics
{
    /// <summary>Single declaring class in scanned source; static candidate, not proof of runtime DI binding.</summary>
    StaticCandidate,

    /// <summary>Explicit MS-DI/Autofac registration; declared binding.</summary>
    DeclaredBinding,

    /// <summary>Explicit registration confirms a single concrete declaring class.</summary>
    Confirmed,
}
