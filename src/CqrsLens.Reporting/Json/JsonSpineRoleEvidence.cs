namespace CqrsLens;

internal sealed class JsonSpineRoleEvidence
{
    public string Role { get; set; } = "";
    public List<JsonSpineEvidence> Evidence { get; set; } = new();
}
