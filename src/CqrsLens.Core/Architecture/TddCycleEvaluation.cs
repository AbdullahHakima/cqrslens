namespace CqrsLens;

/// <summary>
/// Closed-loop evaluation of one CQRS slice combining design verification,
/// architecture checks, static test discovery, and optional runtime TRX results.
/// </summary>
public sealed record TddCycleEvaluation(
    string Phase,
    string DesignStatus,
    bool HasDiscoveredTests,
    bool HasRuntimeResults,
    int RuntimePassed,
    int RuntimeFailed,
    int RuntimeSkipped,
    IReadOnlyList<string> MatchingPassedTests,
    IReadOnlyList<string> MatchingFailedTests,
    IReadOnlyList<SliceDesignCheck> DesignChecks,
    IReadOnlyList<CheckFinding> SliceChecks,
    IReadOnlyList<TestMethodReference> DiscoveredTests,
    IReadOnlyList<string> NextActions,
    string Scope);
