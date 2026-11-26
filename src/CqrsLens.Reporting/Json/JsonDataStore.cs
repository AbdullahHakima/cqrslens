namespace CqrsLens;

/// <summary>Additive JSON representation of a discovered data store or persistence boundary.</summary>
internal sealed class JsonDataStore
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Technology { get; set; } = "";
    public string? Provider { get; set; }
    public string? Project { get; set; }
    public List<string>? Entities { get; set; }
    public List<string>? AccessedBy { get; set; }
    public JsonLocation? Location { get; set; }
}
