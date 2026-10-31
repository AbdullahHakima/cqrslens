namespace CqrsLens;

/// <summary>One mapped property considered when EF Core key conventions are applied.</summary>
internal sealed record EfKeyCandidate(
    string Name,
    bool IsCollection,
    IReadOnlyList<string> Attributes);
