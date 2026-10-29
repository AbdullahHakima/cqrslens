namespace CqrsLens;

internal sealed class JsonSummary
{
    public int Projects { get; set; }
    public int Requests { get; set; }
    public int Handlers { get; set; }
    public int Validators { get; set; }
    public int Behaviors { get; set; }
    public int DbContexts { get; set; }
    public int Notifications { get; set; }
    public int NotificationHandlers { get; set; }
    public int Endpoints { get; set; }
    public int Errors { get; set; }
    public int Warnings { get; set; }
    public int KnownFindings { get; set; }
    public int Diagnostics { get; set; }
}
