namespace CqrsLens;

/// <summary>Structured endpoint/target node reference on a spine relation in the JSON contract.</summary>
internal sealed class JsonSpineNodeRef
{
    public string Kind { get; set; } = "";
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string? ComponentId { get; set; }
    public string? EndpointId { get; set; }
    public string? ProjectId { get; set; }
    public string? SymbolId { get; set; }
    public string? Member { get; set; }
    public string? Reason { get; set; }
}
