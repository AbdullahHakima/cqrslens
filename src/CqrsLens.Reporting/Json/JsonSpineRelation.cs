namespace CqrsLens;

/// <summary>One deduplicated spine relation with full evidence occurrences in the JSON contract.</summary>
internal sealed class JsonSpineRelation
{
    public string Kind { get; set; } = "";
    public JsonSpineNodeRef From { get; set; } = new();
    public JsonSpineNodeRef To { get; set; } = new();
    public List<JsonSpineNodeRef>? Via { get; set; }
    public string? Method { get; set; }
    public string Strength { get; set; } = "";
    public int OccurrenceCount { get; set; }
    public List<JsonSpineEvidence> Occurrences { get; set; } = new();
}
