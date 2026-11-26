namespace CqrsLens;

/// <summary>Discovered data store, ORM, cache, or persistence boundary in the scanned system.</summary>
public sealed class DataStoreInfo
{
    public required string Name { get; init; }
    public required DataStoreKind Kind { get; init; }
    public required string Technology { get; init; }
    public string? Provider { get; set; }
    public string? ProjectName { get; set; }
    public SourceLocation? Location { get; set; }

    public List<string> Entities { get; } = new();
    public List<string> AccessedBy { get; } = new();
}
