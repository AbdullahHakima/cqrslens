namespace CqrsLens;

internal sealed class JsonRequest
{
    public string Name { get; set; } = "";
    public string? Namespace { get; set; }
    public string FullName { get; set; } = "";
    public string Kind { get; set; } = "";
    public string? ResponseType { get; set; }
    public string? ViaMarker { get; set; }
    public string? Project { get; set; }
    public List<string> Handlers { get; set; } = new();
    public List<string> Validators { get; set; } = new();
    public List<JsonRequestProperty>? Properties { get; set; }
    public List<string>? Entrypoints { get; set; }
    public List<string>? TouchedEntities { get; set; }
    public List<string>? PublishedNotifications { get; set; }
    public List<string>? DispatchedRequests { get; set; }
    public JsonLocation? Location { get; set; }
}
