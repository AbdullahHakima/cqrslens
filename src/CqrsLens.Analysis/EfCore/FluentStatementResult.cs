namespace CqrsLens;

/// <summary>Keys and at most one relationship produced by one fluent configuration statement.</summary>
internal readonly record struct FluentStatementResult(
    IReadOnlyList<EfKeyConfiguration> Keys,
    EfRelationshipConfiguration? Relationship)
{
    internal static FluentStatementResult Empty { get; } = new(Array.Empty<EfKeyConfiguration>(), null);
}
