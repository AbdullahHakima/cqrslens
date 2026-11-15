namespace CqrsLens;

/// <summary>
/// Per-fact provenance record attaching an <see cref="EvidenceStrength"/> and optional detail
/// to an individual property or assignment (F0 §5.3).
/// </summary>
public sealed record FactProvenance(
    string Fact,
    EvidenceStrength Strength,
    string? Detail = null);
