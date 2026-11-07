namespace CqrsLens;

/// <summary>
/// The outcome of verifying one claim. <see cref="Verdict"/> is computed from the scan model
/// alone; <see cref="Evidence"/> carries the model facts (with <c>file:line</c> provenance) for a
/// supported or not-supported verdict, and <see cref="Reason"/>/<see cref="Detail"/> explain a
/// not-determinable verdict.
/// </summary>
internal sealed record ClaimCheck(
    ClaimVerdict Verdict,
    string Subject,
    string Relation,
    string? Object,
    string Basis,
    IReadOnlyList<string> Evidence,
    string? Reason,
    string? Detail);
