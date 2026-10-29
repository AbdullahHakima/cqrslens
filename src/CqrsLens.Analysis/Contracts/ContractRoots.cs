namespace CqrsLens;

/// <summary>
/// Contract names the text scanner recognizes without a semantic model. Marker sets
/// are intentionally small: anything else must resolve through source declarations.
/// </summary>
internal static class ContractRoots
{
    public static readonly HashSet<string> HandlerRoots = new(StringComparer.Ordinal)
    {
        "IRequestHandler",
    };

    public static readonly HashSet<string> NotificationMarkerRoots = new(StringComparer.Ordinal)
    {
        "INotification",
        "IDomainEvent",
    };

    public static readonly HashSet<string> NotificationHandlerRoots = new(StringComparer.Ordinal)
    {
        "INotificationHandler",
        "IDomainEventHandler",
    };

    public static readonly HashSet<string> ValidatorRoots = new(StringComparer.Ordinal)
    {
        "AbstractValidator",
        "IValidator",
        // FastEndpoints.Validator<T> is a framework contract that commonly
        // arrives through a package, so it is not always in the scanned source.
        "Validator",
    };

    public static HashSet<string> BuildHandlerRoots(IReadOnlySet<string> handlerMarkers)
    {
        var handlerRoots = new HashSet<string>(HandlerRoots, StringComparer.Ordinal);
        foreach (var marker in handlerMarkers)
            handlerRoots.Add(CSharpScanner.SimpleName(marker));
        return handlerRoots;
    }

    /// <summary>
    /// Roots whose direct declarations are already handled by <see cref="CSharpScanner.ApplyScan"/>.
    /// Indirect-shape discovery must still run for wrapper bases such as ICommandHandler&lt;T&gt;.
    /// </summary>
    public static bool IsDirectShapeRoot(string simpleName)
        => simpleName is "IRequest" or "INotification" or "IDomainEvent" or
            "IRequestHandler" or "INotificationHandler" or "IDomainEventHandler" or
            "AbstractValidator" or "IValidator" or "IPipelineBehavior" or
            "DbContext" or "IdentityDbContext";

    public static bool LooksLikeHandlerContract(string simple)
        => simple.Contains("CommandHandler", StringComparison.Ordinal) ||
           simple.Contains("QueryHandler", StringComparison.Ordinal) ||
           simple.Contains("RequestHandler", StringComparison.Ordinal);

    public static bool LooksLikeValidatorContract(string simple)
        => simple is "Validator" or "AbstractValidator" or "IValidator" ||
           simple.EndsWith("CommandValidator", StringComparison.Ordinal) ||
           simple.EndsWith("QueryValidator", StringComparison.Ordinal);
}
