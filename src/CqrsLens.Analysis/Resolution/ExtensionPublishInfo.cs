namespace CqrsLens;

/// <summary>
/// Metadata for a 1-hop event-publisher extension method that constructs and publishes a
/// domain/integration event via an injected publisher interface.
/// </summary>
internal sealed record ExtensionPublishInfo(
    string InterfaceIdentity,
    string MethodName,
    string InnerPublishMethod,
    string? EventTypeWritten,
    bool UsesMethodTypeArgument,
    CSharpScanner.ClassDecl DeclaringClass,
    SourceFile File);
