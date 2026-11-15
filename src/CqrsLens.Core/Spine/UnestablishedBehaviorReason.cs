namespace CqrsLens;

/// <summary>
/// Why a discovered pipeline behavior could not be given a <see cref="RelationKind.Wraps"/>
/// relation to specific requests under the F0 §4.3 applicability rule.
/// </summary>
public enum UnestablishedBehaviorReason
{
    NoRegistrationSite,
    AssemblyScanningOrExternal,
    GenericConstraintsNotEvaluated,
    ClosedFormUnknown,
}
