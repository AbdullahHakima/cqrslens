namespace CqrsLens;

internal sealed record DbSetMappings(
    Dictionary<string, string> DbSetsByProperty,
    Dictionary<string, string> DbSetsByEntity,
    HashSet<string> DbContextTypeNames);
