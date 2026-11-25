namespace CqrsLens;

/// <summary>
/// One direct non-CQRS operation's sequence diagram (<c>sequenceDiagram</c>),
/// without any synthetic Mediator participant.
/// </summary>
public sealed record DirectSequence(
    string OperationName,
    string DisplayLabel,
    string? ProjectName,
    string KindLabel,
    string Source,
    string Caption);
