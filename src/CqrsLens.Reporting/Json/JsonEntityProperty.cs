namespace CqrsLens;

internal sealed class JsonEntityProperty
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public bool IsKey { get; set; }
    public bool IsForeignKey { get; set; }
    public bool IsNavigation { get; set; }
    public bool IsCollection { get; set; }
    public string? TargetEntity { get; set; }
    public bool IsRequired { get; set; }
    public JsonLocation? Location { get; set; }
}
