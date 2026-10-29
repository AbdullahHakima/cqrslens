using System.Text.RegularExpressions;

namespace CqrsLens;

/// <summary>
/// Builds the DbSet property/entity lookup used to translate code access into a display label
/// such as <c>Orders (Order)</c>. Conflicting simple names are removed instead of choosing one.
/// </summary>
internal static partial class DbSetMappingBuilder
{
    [GeneratedRegex(@"\bDbSet\s*<\s*(?<entity>[A-Za-z_]\w*(?:\s*\.\s*[A-Za-z_]\w*)*)\s*>\s+(?<prop>[A-Za-z_]\w*)\s*\{")]
    private static partial Regex InterfaceDbSetRegex();

    [GeneratedRegex(@"\bIEntityTypeConfiguration\s*<\s*(?<entity>(?:global::)?[A-Za-z_]\w*(?:\s*\.\s*[A-Za-z_]\w*)*)\s*>")]
    private static partial Regex EntityTypeConfigRegex();

    internal static DbSetMappings Build(
        List<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles,
        AnalysisResult result)
    {
        // Map DbSet property names and entity simple names to "Property (Entity)".
        var dbSetsByProperty = new Dictionary<string, string>(StringComparer.Ordinal);
        var dbSetsByEntity = new Dictionary<string, string>(StringComparer.Ordinal);
        var ambiguousDbSetProperties = new HashSet<string>(StringComparer.Ordinal);
        var ambiguousDbSetEntities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ctx in result.DbContexts)
        {
            foreach (var set in ctx.DbSets)
            {
                var entitySimple = CSharpScanner.SimpleName(set.Entity);
                var label = $"{set.Property} ({entitySimple})";
                AddDbSetMapping(dbSetsByProperty, ambiguousDbSetProperties, set.Property, label);
                AddDbSetMapping(dbSetsByEntity, ambiguousDbSetEntities, entitySimple, label);
            }

            foreach (var entity in ctx.Entities)
            {
                AddEntityFallbackMapping(dbSetsByEntity, ambiguousDbSetEntities, entity.Name);
            }
        }

        // Also capture DbSet<T> properties declared on interfaces (e.g., IApplicationDbContext)
        // and IEntityTypeConfiguration<T> declarations.
        foreach (var (file, _, mask) in parsedFiles)
        {
            foreach (Match m in InterfaceDbSetRegex().Matches(file.Code))
            {
                if (!mask.IsCode(m.Index)) continue;
                var prop = m.Groups["prop"].Value.Trim();
                var entitySimple = CSharpScanner.SimpleName(m.Groups["entity"].Value.Trim());
                var label = $"{prop} ({entitySimple})";
                AddDbSetMapping(dbSetsByProperty, ambiguousDbSetProperties, prop, label);
                AddDbSetMapping(dbSetsByEntity, ambiguousDbSetEntities, entitySimple, label);
            }

            foreach (Match m in EntityTypeConfigRegex().Matches(file.Code))
            {
                if (!mask.IsCode(m.Index)) continue;
                var entitySimple = CSharpScanner.SimpleName(m.Groups["entity"].Value.Trim());
                AddEntityFallbackMapping(dbSetsByEntity, ambiguousDbSetEntities, entitySimple);
            }
        }

        var dbContextTypeNames = new HashSet<string>(
            result.DbContexts.Select(c => CSharpScanner.SimpleName(c.Name)),
            StringComparer.Ordinal);

        return new DbSetMappings(dbSetsByProperty, dbSetsByEntity, dbContextTypeNames);
    }

    /// <summary>
    /// Adds a simple-name → display-label mapping, but fails closed when two different
    /// entities share the same simple name or DbSet property name. Guessing the first
    /// one would silently attribute a handler's database work to the wrong entity.
    /// </summary>
    private static void AddDbSetMapping(
        Dictionary<string, string> map,
        HashSet<string> ambiguous,
        string key,
        string label)
    {
        if (string.IsNullOrEmpty(key)) return;
        if (ambiguous.Contains(key)) return;

        if (!map.TryGetValue(key, out var existing))
        {
            map[key] = label;
            return;
        }

        if (string.Equals(existing, label, StringComparison.Ordinal))
            return;

        // An entity-only fallback maps the entity name to itself. A real DbSet label is
        // strictly more informative, so it upgrades the fallback instead of colliding with it.
        if (string.Equals(existing, key, StringComparison.Ordinal))
        {
            map[key] = label;
            return;
        }

        map.Remove(key);
        ambiguous.Add(key);
    }

    /// <summary>
    /// Entity-only fallback (from an EF model or <c>IEntityTypeConfiguration</c>). It never
    /// overrides the more informative DbSet label and stays out of ambiguous keys.
    /// </summary>
    private static void AddEntityFallbackMapping(
        Dictionary<string, string> map,
        HashSet<string> ambiguousKeys,
        string entityName)
    {
        if (string.IsNullOrEmpty(entityName) || ambiguousKeys.Contains(entityName))
            return;
        map.TryAdd(entityName, entityName);
    }
}

internal sealed record DbSetMappings(
    Dictionary<string, string> DbSetsByProperty,
    Dictionary<string, string> DbSetsByEntity,
    HashSet<string> DbContextTypeNames);
