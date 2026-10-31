namespace CqrsLens;

/// <summary>
/// A dispatch-looking call site whose target type could not be statically resolved
/// (a runtime-typed variable, a cast to an interface, a type parameter, an external
/// type). These are counted and reported rather than guessed into an edge: they are
/// the population a request or notification could be reached through even though no
/// resolved call site exists.
/// </summary>
public sealed class UnresolvedDispatch
{
    /// <summary>The member name that looked like a dispatch (Send, PublishAsync, ...).</summary>
    public required string Method { get; init; }

    /// <summary>The receiver identifier (for example <c>_mediator</c>), when one was read.</summary>
    public string? ReceiverName { get; init; }

    public string? ProjectName { get; init; }
    public bool IsTestProject { get; init; }
    public SourceLocation? Location { get; init; }
}
