namespace CqrsLens;

/// <summary>A dispatch-looking call site whose target type could not be resolved.</summary>
internal sealed class JsonUnresolvedDispatch
{
    public string Method { get; set; } = "";
    public string? Receiver { get; set; }
    public string? Project { get; set; }
    public JsonLocation? Location { get; set; }
}
