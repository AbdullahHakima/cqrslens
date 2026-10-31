namespace CqrsLens;

internal sealed class JsonValidator
{
    public string Name { get; set; } = "";
    public string? Namespace { get; set; }
    public string FullName { get; set; } = "";
    public string TargetType { get; set; } = "";
    public string? Project { get; set; }
    public JsonLocation? Location { get; set; }
}
