namespace CqrsLens;

/// <summary>A DbContext whose DbSets, entities or relationships changed.</summary>
internal sealed class ModifiedDbContextDiff
{
    public string Name { get; set; } = "";
    public string? Project { get; set; }
    public List<string> AddedDbSets { get; set; } = new();
    public List<string> RemovedDbSets { get; set; } = new();
    public List<string> AddedEntities { get; set; } = new();
    public List<string> RemovedEntities { get; set; } = new();
    public List<string> AddedRelationships { get; set; } = new();
    public List<string> RemovedRelationships { get; set; } = new();
}
