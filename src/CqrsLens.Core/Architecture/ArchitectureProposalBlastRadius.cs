namespace CqrsLens;

/// <summary>Summary of scanned projects and CQRS artifacts impacted by a proposal.</summary>
public sealed record ArchitectureProposalBlastRadius(
    IReadOnlyList<string> AffectedProjects,
    int AffectedRequests,
    int AffectedHandlers,
    int AffectedValidators,
    int AffectedEndpoints,
    int AffectedDbContexts,
    IReadOnlyList<string> SampleRequests);
