namespace CqrsLens;

/// <summary>One mapped property with its navigation target already resolved by a backend.</summary>
internal readonly record struct EfMappedProperty(
    string Name,
    string Type,
    bool IsPublic,
    bool IsStatic,
    bool IsCollection,
    bool IsNullable,
    string? TargetEntityFullName,
    SourceLocation? Location,
    IReadOnlyList<string> Attributes);
