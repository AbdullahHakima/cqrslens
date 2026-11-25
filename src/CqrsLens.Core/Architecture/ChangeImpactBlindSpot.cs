namespace CqrsLens;

/// <summary>An explicit static-analysis blind spot affecting change impact completeness.</summary>
public sealed record ChangeImpactBlindSpot(
    string Code,
    string Message,
    string? ProjectName = null,
    SourceLocation? Location = null);
