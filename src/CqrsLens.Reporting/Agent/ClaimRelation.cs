namespace CqrsLens;

/// <summary>
/// One relation <c>verify_claim</c> can decide from the scan model alone. A relation is in
/// the vocabulary only when the model holds a fact that can prove or refute it; execution order,
/// error-handling conventions and anything else the scan does not record are deliberately absent.
/// </summary>
/// <param name="Id">Canonical relation id (also the primary spelling).</param>
/// <param name="Display">Human phrasing used in answers.</param>
/// <param name="IsAbsence">True for the negative form (e.g. <c>has no validator</c>).</param>
/// <param name="ObjectRequired">True when the relation is meaningless without an object.</param>
/// <param name="SubjectKinds">Shape kinds the subject may resolve to.</param>
internal sealed record ClaimRelation(
    string Id,
    string Display,
    bool IsAbsence,
    bool ObjectRequired,
    IReadOnlyList<ClaimSubjectKind> SubjectKinds);
