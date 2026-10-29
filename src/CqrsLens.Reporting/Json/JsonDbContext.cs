namespace CqrsLens;

internal sealed class JsonDbContext
{
    public string Name { get; set; } = "";
    public string? Namespace { get; set; }
    public string? Project { get; set; }
    public JsonLocation? Location { get; set; }
    public List<JsonDbSet> DbSets { get; set; } = new();
    public List<JsonEntity> Entities { get; set; } = new();
    public List<JsonEntityRelationship> Relationships { get; set; } = new();
}
