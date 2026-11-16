namespace CqrsLens;

internal sealed class JsonUnestablishedBehavior
{
    public string? BehaviorComponentId { get; set; }
    public string BehaviorName { get; set; } = "";
    public string Reason { get; set; } = "";
    public string? RegistrationSite { get; set; }
    public string? Project { get; set; }
    public JsonLocation? Location { get; set; }
}
