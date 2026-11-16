namespace CqrsLens;

internal sealed class JsonSpineEndpoint
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string HttpMethod { get; set; } = "";
    public string Route { get; set; } = "";
    public string VerbStrength { get; set; } = "";
    public string RouteStrength { get; set; } = "";
    public bool Inferred { get; set; }
    public string? Project { get; set; }
    public JsonLocation? Location { get; set; }
    public List<JsonSpineEvidence>? Evidence { get; set; }
}
