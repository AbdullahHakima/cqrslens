namespace CqrsLens;

public sealed class CqrsRequest
{
    public required string Name { get; init; }
    public string Namespace { get; set; } = "";
    public List<string> ContainingTypes { get; } = new();

    public string FullName =>
        TypeFullName.Compose(Namespace, ContainingTypes, Name);

    public string? ResponseType { get; set; }
    public string? ProjectName { get; set; }
    public SourceLocation? Location { get; set; }

    /// <summary>
    /// Marker interface through which this request was discovered when it does not
    /// directly implement IRequest (null for direct IRequest requests).
    /// </summary>
    public string? ViaMarker { get; set; }

    private string? _kindOverride;

    public string Kind
    {
        get
        {
            if (_kindOverride is not null) return _kindOverride;
            return Name.EndsWith("Command", StringComparison.Ordinal) ? "Command" :
                   Name.EndsWith("Query", StringComparison.Ordinal) ? "Query" : "Request";
        }
        set => _kindOverride = value;
    }

    internal ComponentId? SpineId { get; set; }

    public SpineLinkList Handlers { get; } = new(SpineLinkKind.RequestHandlers);
    public SpineLinkList Validators { get; } = new(SpineLinkKind.RequestValidators);
    public List<RequestPropertyInfo> Properties { get; } = new();
    public SpineLinkList Entrypoints { get; } = new(SpineLinkKind.RequestEntrypoints);
    public SpineLinkList TouchedEntities { get; } = new(SpineLinkKind.RequestTouchedEntities);
    public SpineLinkList PublishedNotifications { get; } = new(SpineLinkKind.RequestPublishedNotifications);
    public SpineLinkList DispatchedRequests { get; } = new(SpineLinkKind.RequestDispatchedRequests);

    internal void AttachSpine(AnalysisResult result, ComponentId id)
    {
        SpineId = id;
        NodeReference? Owner() => SpineId is null ? null : NodeReference.ForComponent(SpineId);
        Handlers.Attach(result, Owner);
        Validators.Attach(result, Owner);
        Entrypoints.Attach(result, Owner);
        TouchedEntities.Attach(result, Owner);
        PublishedNotifications.Attach(result, Owner);
        DispatchedRequests.Attach(result, Owner);
    }
}
