namespace CqrsLens;

/// <summary>Additive JSON representation of one project's runtime role and capabilities.</summary>
internal sealed class JsonServiceProfile
{
    public string ProjectName { get; set; } = "";
    public string Layer { get; set; } = "Other";
    public string RuntimeRole { get; set; } = "";
    public List<string> Technologies { get; set; } = new();
    public List<string> EntryFiles { get; set; } = new();
    public int HttpEndpointCount { get; set; }
    public int GrpcEndpointCount { get; set; }
    public int WorkerCount { get; set; }
    public int MessageConsumerCount { get; set; }
    public int RequestCount { get; set; }
    public int DbContextCount { get; set; }
    public int PublishedEventCount { get; set; }
    public int ConsumedEventCount { get; set; }
}
