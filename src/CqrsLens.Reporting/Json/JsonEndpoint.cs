namespace CqrsLens;

internal sealed class JsonEndpoint
{
    public string Name { get; set; } = "";
    public string HttpMethod { get; set; } = "";
    public string Route { get; set; } = "";
    public string TargetRequest { get; set; } = "";
    public string? TargetRequestFullName { get; set; }
    public bool Inferred { get; set; }
    public string? Project { get; set; }
    public JsonLocation? Location { get; set; }
}
