namespace CqrsLens;

/// <summary>
/// Memoized non-EF Core data store and entity accesses detected for a single class declaration.
/// </summary>
internal sealed class ClassDataAccess
{
    public List<string> DataStores { get; } = new();
    public List<string> Entities { get; } = new();
}
