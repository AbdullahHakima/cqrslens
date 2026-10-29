namespace CqrsLens;

internal sealed class JsonDiagnostic
{
    public string Code { get; set; } = "";
    public string Severity { get; set; } = "";
    public string Message { get; set; } = "";
    public string? Project { get; set; }
    public JsonLocation? Location { get; set; }
}
