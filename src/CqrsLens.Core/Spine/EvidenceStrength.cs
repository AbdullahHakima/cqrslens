namespace CqrsLens;

/// <summary>
/// Per-fact certainty classification: direct static observation, index/registration resolution,
/// or heuristic/convention inference (F0 §5.2, §5.3).
/// </summary>
public enum EvidenceStrength
{
    Proven,
    Resolved,
    Inferred,
}
