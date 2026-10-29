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

    public List<string> Handlers { get; } = new();
    public List<string> Validators { get; } = new();
    public List<RequestPropertyInfo> Properties { get; } = new();
    public List<string> Entrypoints { get; } = new();
    public List<string> TouchedEntities { get; } = new();
    public List<string> PublishedNotifications { get; } = new();
    public List<string> DispatchedRequests { get; } = new();
}
