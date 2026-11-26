namespace CqrsLens;

/// <summary>Capability, technology-stack, and runtime-role profile for one scanned project.</summary>
public sealed class ServiceProfileInfo
{
    public required string ProjectName { get; init; }
    public string Layer { get; set; } = "Other";
    public ServiceRuntimeRole RuntimeRole { get; set; }

    public List<string> Technologies { get; } = new();
    public List<string> EntryFiles { get; } = new();

    public int HttpEndpointCount { get; set; }
    public int GrpcEndpointCount { get; set; }
    public int WorkerCount { get; set; }
    public int MessageConsumerCount { get; set; }
    public int RequestCount { get; set; }
    public int DbContextCount { get; set; }
    public int DataStoreCount { get; set; }
    public int PublishedEventCount { get; set; }
    public int ConsumedEventCount { get; set; }
}
