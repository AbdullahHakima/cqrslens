namespace CqrsLens;

/// <summary>
/// A cross-project integration flow discovered from integration event pub/sub,
/// .NET Aspire orchestration wiring, typed HTTP/gRPC clients, or shared DbContexts.
/// </summary>
public sealed class IntegrationFlowEdge
{
    public required IntegrationFlowKind Kind { get; init; }
    public required string FromProject { get; init; }
    public required string ToProject { get; init; }
    public required string Label { get; init; }
    public string? MessageType { get; set; }
    public SourceLocation? Location { get; set; }
}
