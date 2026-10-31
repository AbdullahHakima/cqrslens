namespace CqrsLens;

public sealed class CqrsNotification
{
    public required string Name { get; init; }
    public string Namespace { get; set; } = "";
    public List<string> ContainingTypes { get; } = new();

    public string FullName =>
        TypeFullName.Compose(Namespace, ContainingTypes, Name);

    public string? ProjectName { get; set; }
    public SourceLocation? Location { get; set; }

    /// <summary>
    /// Marker interface or base type through which this notification was discovered when it
    /// does not directly implement INotification (e.g. IDomainEvent or DomainEventBase).
    /// </summary>
    public string? ViaMarker { get; set; }

    public List<string> Handlers { get; } = new();
}
