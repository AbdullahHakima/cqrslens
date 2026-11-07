namespace CqrsLens;

/// <summary>Exactly one of the three verdicts <c>verify_claim</c> may return.</summary>
internal enum ClaimVerdict
{
    Supported,
    NotSupported,
    NotDeterminable,
}
