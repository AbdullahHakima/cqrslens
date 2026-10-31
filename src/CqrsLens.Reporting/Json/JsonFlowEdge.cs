namespace CqrsLens;

/// <summary>One solution-wide flow edge in the JSON contract.</summary>
internal sealed class JsonFlowEdge
{
    /// <summary>endpointToRequest | requestToHandler | handlerToRequest | handlerToNotification | handlerToDbContext.</summary>
    public string Kind { get; set; } = "";
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string? Project { get; set; }
    public JsonLocation? Location { get; set; }
}
