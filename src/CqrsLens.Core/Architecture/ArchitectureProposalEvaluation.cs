namespace CqrsLens;

/// <summary>The result of checking changes against a snapshot of the project reference graph.</summary>
public sealed record ArchitectureProposalEvaluation(
    string Status,
    int ExistingProjectCount,
    IReadOnlyList<ArchitectureProposalProject> ProposedProjects,
    IReadOnlyList<ArchitectureProposalReference> ProposedReferences,
    IReadOnlyList<ArchitectureProposalFinding> Findings,
    string Scope = "Project references from the static scan. Review build conditions, runtime dependencies, and domain boundaries separately.",
    IReadOnlyList<ArchitectureProposalProjectMetrics>? Metrics = null,
    ArchitectureProposalBlastRadius? BlastRadius = null);
