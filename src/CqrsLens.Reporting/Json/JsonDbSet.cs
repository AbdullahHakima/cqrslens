namespace CqrsLens;

internal sealed class JsonDbSet
{
    public string Property { get; set; } = "";
    public string Entity { get; set; } = "";
    public string? EntityFullName { get; set; }
}
