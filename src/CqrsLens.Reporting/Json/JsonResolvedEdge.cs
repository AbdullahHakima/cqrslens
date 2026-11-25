namespace CqrsLens;

/// <summary>
/// One DI-resolved edge in the JSON contract: a hop that exists because the injected
/// interface's implementation was resolved through the service-resolution index, not
/// because the source wrote the target directly. The interface is named in
/// <see cref="Via"/> so a consumer can always tell a resolved hop from a direct one.
/// </summary>
internal sealed class JsonResolvedEdge
{
    /// <summary>handlerToDbContextResolved (only kind today).</summary>
    public string Kind { get; set; } = "";

    public string From { get; set; } = "";

    /// <summary>Full name of the interface or initial service the hop resolves through.</summary>
    public string Via { get; set; } = "";

    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? ViaChain { get; set; }

    public string To { get; set; } = "";
    public string? Project { get; set; }
    public JsonLocation? Location { get; set; }
}
