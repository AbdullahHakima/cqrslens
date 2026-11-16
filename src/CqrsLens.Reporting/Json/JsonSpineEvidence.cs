namespace CqrsLens;

/// <summary>Structured evidence observation backing a spine relation in the JSON contract.</summary>
internal sealed class JsonSpineEvidence
{
    public string Rule { get; set; } = "";
    public string Origin { get; set; } = "";
    public string Strength { get; set; } = "";
    public JsonLocation? Location { get; set; }
    public string? LocationOmittedReason { get; set; }
    public List<JsonSpineNodeRef>? Via { get; set; }
    public string? ResolutionSemantics { get; set; }
    public string? ScopeProject { get; set; }
    public bool? TestProject { get; set; }
    public string? Detail { get; set; }
}
