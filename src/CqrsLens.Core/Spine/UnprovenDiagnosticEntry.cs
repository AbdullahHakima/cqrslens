namespace CqrsLens;

/// <summary>
/// An unproven ambiguous link or unresolved contract/entity/navigation observation recorded
/// in the unproven populations rather than guessed into a relation (F0 §5.4).
/// </summary>
public sealed record UnprovenDiagnosticEntry(
    UnprovenItemKind Kind,
    string Code,
    string Subject,
    string Message,
    SourceLocation? Location = null,
    string? ProjectName = null);
