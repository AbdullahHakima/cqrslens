namespace CqrsLens;

/// <summary>An entity class in one EF Core model.</summary>
public sealed class EntityInfo
{
    public required string Name { get; init; }
    public string Namespace { get; set; } = "";
    public List<string> ContainingTypes { get; } = new();
    public string? ProjectName { get; set; }
    public SourceLocation? Location { get; set; }

    public string FullName =>
        TypeFullName.Compose(Namespace, ContainingTypes, Name);

    public List<EntityPropertyInfo> Properties { get; } = new();
}
