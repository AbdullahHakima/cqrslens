namespace CqrsLens;

internal sealed class JsonNotificationHandler
{
    public string Name { get; set; } = "";
    public string? Namespace { get; set; }
    public string FullName { get; set; } = "";
    public string NotificationName { get; set; } = "";
    public List<string>? Dependencies { get; set; }
    public List<string>? TouchedEntities { get; set; }
    public List<string>? PublishedNotifications { get; set; }
    public List<string>? DispatchedRequests { get; set; }
    public string? Project { get; set; }
    public JsonLocation? Location { get; set; }
}
