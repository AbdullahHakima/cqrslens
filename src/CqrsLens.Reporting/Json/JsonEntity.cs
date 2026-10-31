namespace CqrsLens;

internal sealed class JsonEntity
{
    public string Name { get; set; } = "";
    public string? Namespace { get; set; }
    public string FullName { get; set; } = "";
    public string? Project { get; set; }
    public JsonLocation? Location { get; set; }
    public List<JsonEntityProperty> Properties { get; set; } = new();
}
