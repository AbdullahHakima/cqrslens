namespace CqrsLens;

/// <summary>
/// One interface → implementation entry of the service-resolution index. The list of
/// these lives on <see cref="AnalysisResult.ServiceResolutions"/> and is additive
/// model data: nothing here changes an existing count, finding or report field.
/// </summary>
public sealed class ServiceResolution
{
    /// <summary>
    /// Canonical signature key. A non-generic interface is its full name
    /// (<c>Northwind.Infrastructure.INorthwindDbContext</c>); an open generic keeps the
    /// arity suffix (<c>MediatR.IPipelineBehavior`2</c>); a closed generic appends its
    /// substituted arguments (<c>MediatR.IPipelineBehavior`2&lt;CreateOrder, int&gt;</c>).
    /// </summary>
    public required string Interface { get; init; }

    /// <summary>The interface's full dotted name, without the arity suffix or arguments.</summary>
    public required string InterfaceName { get; init; }

    /// <summary>Internal identity of the interface declaration (name plus `arity when generic).</summary>
    public required string Identity { get; init; }

    /// <summary>Number of declared generic parameters (0 for a non-generic interface).</summary>
    public int Arity { get; init; }

    /// <summary>True when this entry covers the open generic form, not a closed one.</summary>
    public bool IsOpenGeneric { get; init; }

    /// <summary>Substituted generic arguments for a closed generic entry; empty otherwise.</summary>
    public List<string> Arguments { get; } = new();

    /// <summary>Every declaration/registration candidate seen for this interface.</summary>
    public List<ServiceCandidate> Candidates { get; } = new();

    /// <summary>
    /// The chosen implementation full name, or null when the interface has no candidate
    /// or the candidate set is ambiguous.
    /// </summary>
    public string? Implementation { get; set; }

    /// <summary>Where <see cref="Implementation"/> came from.</summary>
    public ServiceResolutionSource Source { get; set; }

    /// <summary>
    /// True when two or more candidates were seen with no registration (or conflicting
    /// registrations) to disambiguate. Fail-closed: no implementation is chosen.
    /// </summary>
    public bool IsAmbiguous { get; set; }

    /// <summary>Location of the interface declaration, when it is in the scanned source.</summary>
    public SourceLocation? InterfaceLocation { get; set; }
}
