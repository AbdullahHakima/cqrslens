namespace CqrsLens;

/// <summary>
/// An interface reached from a concrete class's base-class/interface chain, with the
/// class's generic parameters substituted into the written arguments. Produced by
/// <see cref="TypeHierarchy.CollectImplementedInterfaces"/> for the resolution index.
/// </summary>
internal sealed record ImplementedInterface(
    string Identity,
    int Arity,
    IReadOnlyList<string> Arguments,
    bool IsOpenGeneric);
