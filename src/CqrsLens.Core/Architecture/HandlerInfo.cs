namespace CqrsLens;

public sealed class HandlerInfo
{
    public required string Name { get; init; }
    public string Namespace { get; set; } = "";
    public List<string> ContainingTypes { get; } = new();

    public string FullName =>
        TypeFullName.Compose(Namespace, ContainingTypes, Name);

    public required string RequestName { get; init; }
    public List<string> Dependencies { get; } = new();
    public List<string> TouchedEntities { get; } = new();
    public List<string> PublishedNotifications { get; } = new();
    public List<string> DispatchedRequests { get; } = new();

    // Set when the request name in IRequestHandler<...> could be resolved to a namespace.
    public string? RequestNamespace { get; set; }

    /// <summary>Namespaces imported by the declaring file, used by the linker.</summary>
    internal List<string> ImportedNamespaces { get; } = new();

    public string? ProjectName { get; set; }
    public SourceLocation? Location { get; set; }
}
