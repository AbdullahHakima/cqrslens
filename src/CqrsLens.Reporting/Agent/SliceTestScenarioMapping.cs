namespace CqrsLens;

/// <summary>Heuristic mapping of an authored acceptance scenario to a slice boundary and test level.</summary>
internal sealed record SliceTestScenarioMapping(
    string ScenarioName,
    string RecommendedBoundary,
    string Level,
    string Confidence,
    string Rationale);
