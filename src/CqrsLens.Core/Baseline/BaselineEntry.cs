namespace CqrsLens;

public sealed class BaselineEntry
{
    public string Code { get; set; } = "";
    public string? Target { get; set; }
    public string? File { get; set; }
    public string? Project { get; set; }
    public string? Severity { get; set; }
    public string? Message { get; set; }
    public int? Line { get; set; }
    public int? Column { get; set; }
}
