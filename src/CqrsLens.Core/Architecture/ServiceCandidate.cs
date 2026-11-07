namespace CqrsLens;

/// <summary>
/// One implementation candidate for a service interface. A candidate is either a
/// concrete class that declares the interface (declaration index) or the
/// implementation named by an explicit DI registration, or both.
/// </summary>
public sealed class ServiceCandidate
{
    /// <summary>Implementation type: full name when declared in the scan, otherwise as written.</summary>
    public required string Type { get; init; }

    /// <summary>True when a concrete class with this name is declared in the scanned source.</summary>
    public bool DeclaredInScan { get; init; }

    /// <summary>True when an explicit DI registration named this implementation.</summary>
    public bool FromRegistration { get; init; }

    /// <summary>Location of the class declaration (declaration candidates) or the registration.</summary>
    public SourceLocation? Location { get; init; }
}
