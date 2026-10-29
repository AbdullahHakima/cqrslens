namespace CqrsLens;

internal sealed class JsonLocation
{
    public string File { get; set; } = "";
    public int Line { get; set; }
    public int Column { get; set; }
}
