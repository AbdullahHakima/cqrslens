namespace CqrsLens;

internal sealed class JsonNotification
{
    public string Name { get; set; } = "";
    public string? Namespace { get; set; }
    public string FullName { get; set; } = "";
    public string? ViaMarker { get; set; }
    public string? Project { get; set; }
    public List<string> Handlers { get; set; } = new();
    public JsonLocation? Location { get; set; }
}
