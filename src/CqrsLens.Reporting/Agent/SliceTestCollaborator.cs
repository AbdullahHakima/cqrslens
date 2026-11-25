namespace CqrsLens;

/// <summary>One handler constructor collaborator with DI resolution and called methods.</summary>
internal sealed record SliceTestCollaborator(
    string DeclaredType,
    string? ResolvedType,
    string ResolutionSource,
    IReadOnlyList<string> CalledMethods,
    string RecommendedDouble);
