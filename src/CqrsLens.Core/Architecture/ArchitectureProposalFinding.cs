namespace CqrsLens;

/// <summary>Feedback on a proposed change, with existing code evidence where available.</summary>
public sealed record ArchitectureProposalFinding(
    string Code,
    string Severity,
    string Message,
    string Suggestion,
    string? File = null,
    IReadOnlyList<string>? DependencyPath = null,
    IReadOnlyList<string>? Candidates = null,
    ArchitectureProposalReference? Reference = null);
