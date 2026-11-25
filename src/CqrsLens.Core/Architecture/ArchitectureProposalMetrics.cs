namespace CqrsLens;

/// <summary>Robert C. Martin coupling and instability metrics for a project before and after a proposal.</summary>
public sealed record ArchitectureProposalProjectMetrics(
    string Project,
    string Layer,
    int AfferentBefore,
    int AfferentAfter,
    int EfferentBefore,
    int EfferentAfter,
    double InstabilityBefore,
    double InstabilityAfter);
