namespace CqrsLens;

/// <summary>Additive JSON representation of a discovered system-wide entrypoint.</summary>
internal sealed class JsonSystemEntrypoint
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string ExecutionStyle { get; set; } = "";
    public string Method { get; set; } = "";
    public string RouteOrTrigger { get; set; } = "";
    public string? Project { get; set; }
    public bool Inferred { get; set; }
    public List<string>? TargetRequests { get; set; }
    public List<string>? CalledServices { get; set; }
    public List<string>? TouchedDbContexts { get; set; }
    public List<string>? TouchedDataStores { get; set; }
    public List<string>? TouchedEntities { get; set; }
    public List<string>? PublishedMessages { get; set; }
    public JsonLocation? Location { get; set; }
}
