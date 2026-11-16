namespace CqrsLens;

/// <summary>Read-only human intent declaration loaded from <c>.cqrslens.json</c> (F3 R-INTENT / R4).</summary>
public sealed record IntentSpecification(
    IReadOnlyList<IntentModuleDefinition> Modules,
    IReadOnlyList<IntentBoundaryRule> Boundaries,
    IntentPublicSurface? PublicSurface,
    IReadOnlyDictionary<string, string> Ownership);
