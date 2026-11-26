namespace CqrsLens;

/// <summary>
/// Memoized per-class direct dependency, persistence, and event summary reused across
/// multi-hop service traversals so shared services are scanned at most once.
/// </summary>
internal sealed class CachedClassEffects
{
    public List<string> DirectCalledServices { get; } = new();
    public List<string> DirectDbContexts { get; } = new();
    public List<string> DirectDataStores { get; } = new();
    public List<string> DirectEntities { get; } = new();
    public List<string> DirectPublishedMessages { get; } = new();
    public List<CSharpScanner.ClassDecl> NextServiceClasses { get; } = new();
}
