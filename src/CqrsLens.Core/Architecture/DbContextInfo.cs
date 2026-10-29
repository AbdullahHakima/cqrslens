namespace CqrsLens;

public sealed class DbContextInfo
{
    public required string Name { get; init; }
    public string Namespace { get; set; } = "";
    public List<string> ContainingTypes { get; } = new();
    public string? ProjectName { get; set; }
    public SourceLocation? Location { get; set; }
    public List<DbSetInfo> DbSets { get; } = new();

    /// <summary>Entities reachable from this context's DbSets through navigations.</summary>
    public List<EntityInfo> Entities { get; } = new();

    /// <summary>Relationships discovered for <see cref="Entities"/>.</summary>
    public List<EntityRelationshipInfo> Relationships { get; } = new();
}
