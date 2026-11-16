namespace CqrsLens;

/// <summary>Declared architectural module grouping from <c>.cqrslens.json</c> (R4).</summary>
public sealed record IntentModuleDefinition(
    string Name,
    IReadOnlyList<string> Projects,
    IReadOnlyList<string> Components);
