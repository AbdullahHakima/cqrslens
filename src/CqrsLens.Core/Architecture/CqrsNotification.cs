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

    internal ComponentId? SpineId { get; set; }

    public SpineLinkList Handlers { get; } = new(SpineLinkKind.NotificationHandlers);

    internal void AttachSpine(AnalysisResult result, ComponentId id)
    {
        SpineId = id;
        Handlers.Attach(result, () => SpineId is null ? null : NodeReference.ForComponent(SpineId));
    }
}
