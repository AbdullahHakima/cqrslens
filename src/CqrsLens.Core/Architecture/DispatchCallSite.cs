namespace CqrsLens;

/// <summary>
/// A dispatch call (<c>Send</c>/<c>Publish</c>/<c>InvokeAsync</c> and their async
/// variants) whose target type was statically resolved to an inventoried request or
/// notification. The sender may be a handler, an endpoint, or any other class; the
/// call site is what makes the target production-reachable.
/// </summary>
public sealed class DispatchCallSite
{
    public required string TargetFullName { get; init; }
    public required DispatchTargetKind TargetKind { get; init; }

    /// <summary>Full name of the declaring class when it could be determined.</summary>
    public string? SenderFullName { get; init; }

    public string? ProjectName { get; init; }

    /// <summary>True when the call site lives in a test project; such sends are not production reachability.</summary>
    public bool IsTestProject { get; init; }

    public SourceLocation? Location { get; init; }
}
