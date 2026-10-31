namespace CqrsLens;

/// <summary>A resolved dispatch call site, including senders that are not modelled edges.</summary>
internal sealed class JsonDispatchCallSite
{
    public string Target { get; set; } = "";
    public string TargetKind { get; set; } = "";
    public string? Sender { get; set; }
    public string? Project { get; set; }
    public bool TestProject { get; set; }
    public JsonLocation? Location { get; set; }
}
