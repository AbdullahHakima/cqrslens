namespace CqrsLens;

public sealed class HandlerInfo
{
    public required string Name { get; init; }
    public string Namespace { get; set; } = "";
    public List<string> ContainingTypes { get; } = new();

    public string FullName =>
        TypeFullName.Compose(Namespace, ContainingTypes, Name);

    internal ComponentId? SpineId { get; set; }

    public required string RequestName { get; init; }
    public List<string> Dependencies { get; } = new();
    public SpineLinkList TouchedEntities { get; } = new(SpineLinkKind.HandlerTouchedEntities);
    public SpineLinkList PublishedNotifications { get; } = new(SpineLinkKind.HandlerPublishedNotifications);
    public SpineLinkList DispatchedRequests { get; } = new(SpineLinkKind.HandlerDispatchedRequests);

    // Set when the request name in IRequestHandler<...> could be resolved to a namespace.
    public string? RequestNamespace { get; set; }

    /// <summary>Namespaces imported by the declaring file, used by the linker.</summary>
    internal List<string> ImportedNamespaces { get; } = new();

    public string? ProjectName { get; set; }
    public SourceLocation? Location { get; set; }

    internal void AttachSpine(AnalysisResult result, ComponentId id)
    {
        SpineId = id;
        NodeReference? Owner() => SpineId is null ? null : NodeReference.ForComponent(SpineId);
        TouchedEntities.Attach(result, Owner);
        PublishedNotifications.Attach(result, Owner);
        DispatchedRequests.Attach(result, Owner);
    }
}
