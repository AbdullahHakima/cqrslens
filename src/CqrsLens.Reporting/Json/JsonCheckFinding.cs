namespace CqrsLens;

internal sealed class JsonCheckFinding
{
    public string Code { get; set; } = "";
    public string Severity { get; set; } = "";
    public string Message { get; set; } = "";
    public string? Target { get; set; }
    public string? Project { get; set; }
    public bool Known { get; set; }
    public JsonLocation? Location { get; set; }
}
