namespace CqrsLens;

/// <summary>Comparison of a saved design against the source model produced by a scan.</summary>
public sealed record SliceDesignEvaluation(
    string Status,
    SliceDesign Design,
    IReadOnlyList<SliceDesignCheck> Checks,
    string Scope = "Static source model. Run the acceptance tests to verify behavior and runtime wiring.");
