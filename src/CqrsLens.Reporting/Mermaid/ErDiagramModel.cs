namespace CqrsLens;

/// <summary>The parsed shape of a Mermaid ER diagram before layout.</summary>
internal sealed class ErDiagramModel
{
    public string? AccTitle { get; set; }
    public string? AccDescr { get; set; }
    public List<ErEntityBox> Entities { get; } = new();
    public Dictionary<string, ErEntityBox> EntityById { get; } = new(StringComparer.Ordinal);
    public List<ErRelationshipEdge> Relationships { get; } = new();
}
