namespace CqrsLens;

/// <summary>
/// Result of resolving an injected field, property, or constructor parameter type through the
/// DI resolution index (closed-generic first, then open-generic fallback, then concrete class).
/// </summary>
internal sealed record ResolvedServiceTarget(
    bool IsResolved,
    bool IsConcrete,
    bool IsAmbiguous,
    bool IsDbContext,
    bool IsUnimplementedInterface,
    string InterfaceSignature,
    string InterfaceIdentity,
    IReadOnlyList<string> InterfaceTypeArgs,
    SourceLocation? InterfaceLocation,
    string ImplementationFullName,
    int ImplementationArity);
