namespace CqrsLens;

/// <summary>Expected DbContexts and entities touched by a CQRS slice.</summary>
public sealed record SliceDesignPersistence(
    IReadOnlyList<string>? DbContexts = null,
    IReadOnlyList<string>? Entities = null);
