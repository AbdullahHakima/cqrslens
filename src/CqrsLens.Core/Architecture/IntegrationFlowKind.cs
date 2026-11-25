namespace CqrsLens;

/// <summary>Kind of cross-project or cross-service integration link.</summary>
public enum IntegrationFlowKind
{
    IntegrationEvent,
    AspireReference,
    HttpClient,
    GrpcClient,
    SharedDbContext,
}
