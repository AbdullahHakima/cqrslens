namespace CqrsLens;

internal sealed class JsonUnprovenDiagnostic
{
    public string Kind { get; set; } = "";
    public string Code { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Message { get; set; } = "";
    public string? Project { get; set; }
    public JsonLocation? Location { get; set; }
}
