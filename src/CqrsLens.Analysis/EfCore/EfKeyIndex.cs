namespace CqrsLens;

/// <summary>
/// Caches and applies EF Core key rules: explicit Fluent keys first, then [Key],
/// then the <c>Id</c> / <c>&lt;Entity&gt;Id</c> conventions.
/// </summary>
internal sealed class EfKeyIndex
{
    private readonly Dictionary<string, List<string>> _configuredKeys;
    private readonly Dictionary<string, List<string>> _cache = new(StringComparer.Ordinal);

    internal EfKeyIndex(EfFluentConfiguration fluent)
    {
        _configuredKeys = fluent.Keys
            .GroupBy(k => k.EntityFullName, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.SelectMany(k => k.Properties)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                StringComparer.Ordinal);
    }

    internal IReadOnlyList<string> Resolve(
        string entityFullName,
        string entityName,
        IReadOnlyList<EfKeyCandidate> properties)
    {
        if (_cache.TryGetValue(entityFullName, out var cached))
            return cached;

        var configured = _configuredKeys.TryGetValue(entityFullName, out var keys) && keys.Count > 0
            ? keys
            : null;
        var list = Select(entityName, configured, properties).ToList();
        _cache[entityFullName] = list;
        return list;
    }

    private static IReadOnlyList<string> Select(
        string entityName,
        IReadOnlyList<string>? configured,
        IReadOnlyList<EfKeyCandidate> properties)
    {
        if (configured is { Count: > 0 })
            return configured;

        var annotated = properties
            .Where(p => EfAnnotations.HasAttribute(p.Attributes, "Key"))
            .Select(p => p.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (annotated.Count > 0)
            return annotated;

        var byId = properties.FirstOrDefault(p =>
            string.Equals(p.Name, "Id", StringComparison.OrdinalIgnoreCase) &&
            !p.IsCollection);
        if (byId is not null)
            return new List<string> { byId.Name };

        var conventionalName = entityName + "Id";
        var byName = properties.FirstOrDefault(p =>
            string.Equals(p.Name, conventionalName, StringComparison.OrdinalIgnoreCase) &&
            !p.IsCollection);
        return byName is null ? Array.Empty<string>() : new List<string> { byName.Name };
    }
}
