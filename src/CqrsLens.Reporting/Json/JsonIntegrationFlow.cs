namespace CqrsLens;

/// <summary>Additive JSON representation of a cross-project integration flow edge.</summary>
internal sealed class JsonIntegrationFlow
{
    public string Kind { get; set; } = "";
    public string FromProject { get; set; } = "";
    public string ToProject { get; set; } = "";
    public string Label { get; set; } = "";
    public string? MessageType { get; set; }
    public JsonLocation? Location { get; set; }
}
