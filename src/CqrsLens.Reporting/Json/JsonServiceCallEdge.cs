namespace CqrsLens;

/// <summary>
/// One method-body service-call edge in the JSON contract: a source class invoking a method on
/// one of its injected members. <see cref="Via"/> is the member's declared type (the interface
/// the hop resolves through, or a concrete class); <see cref="To"/> is the resolved target.
/// Additive section, emitted only when non-empty.
/// </summary>
internal sealed class JsonServiceCallEdge
{
    /// <summary>Always <c>serviceCall</c> today.</summary>
    public string Kind { get; set; } = "";

    public string From { get; set; } = "";

    /// <summary>The injected member's declared type (interface full name, or concrete class).</summary>
    public string Via { get; set; } = "";

    /// <summary>The invoked method's name (no arguments, no signature).</summary>
    public string Method { get; set; } = "";

    public string To { get; set; } = "";
    public string? Project { get; set; }
    public JsonLocation? Location { get; set; }
}
