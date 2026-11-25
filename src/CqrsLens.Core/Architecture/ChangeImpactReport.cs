namespace CqrsLens;

/// <summary>Bounded bidirectional dependency and test-coverage impact report for a target symbol or project.</summary>
public sealed record ChangeImpactReport(
    string Status,
    string Confidence,
    int MaxDepth,
    ChangeImpactTarget? Target,
    IReadOnlyList<ChangeImpactTarget> Candidates,
    IReadOnlyList<ChangeImpactItem> ImpactedItems,
    IReadOnlyList<string> AffectedProjects,
    IReadOnlyList<TestMethodReference> ExistingTestReferences,
    IReadOnlyList<string> UncoveredHotspots,
    IReadOnlyList<ChangeImpactBlindSpot> BlindSpots,
    string Scope = "Static dependency and flow impact trace. Runtime reflection, dynamic dispatch, and conditional build references are surfaced as blind spots.");
