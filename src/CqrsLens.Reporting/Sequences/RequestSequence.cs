namespace CqrsLens;

/// <summary>
/// One request's sequence diagram: the Mermaid source plus its caption. Built only
/// from data the scan model already holds (Stage 3 brief S1).
/// </summary>
internal sealed record RequestSequence(
    string RequestName,
    string RequestFullName,
    string Source,
    string Caption);
