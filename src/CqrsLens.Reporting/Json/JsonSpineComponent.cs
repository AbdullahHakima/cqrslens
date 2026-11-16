namespace CqrsLens;

internal sealed class JsonSpineComponent
{
    public string Id { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Project { get; set; }
    public JsonLocation? Location { get; set; }
    public List<JsonSpineRoleEvidence> Roles { get; set; } = new();
}
