namespace CqrsLens;

/// <summary>
/// Where a <see cref="ServiceResolution"/> got its chosen implementation from.
/// The value is additive data: it explains the edge without changing any existing
/// count or finding.
/// </summary>
public enum ServiceResolutionSource
{
    /// <summary>No implementation could be chosen (no candidate, or ambiguous).</summary>
    None,

    /// <summary>A single concrete class declaration implements the interface.</summary>
    Declaration,

    /// <summary>An explicit DI registration named the implementation.</summary>
    Registration,

    /// <summary>A registration named a class that also declares the interface.</summary>
    Both,
}
