namespace CqrsLens;

/// <summary>
/// Extracts the small, common Fluent API surface that affects an ER diagram:
/// HasKey, HasOne/HasMany, WithOne/WithMany, HasForeignKey and IsRequired.
/// Unsupported builder calls are ignored; convention/annotation discovery still
/// applies, and the README documents the boundary.
/// </summary>
internal static class EfFluentConfigurationParser
{
    public static EfFluentConfiguration ParseText(
        IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan)> files,
        TypeHierarchy hierarchy,
        AnalysisResult result,
        IReadOnlyDictionary<string, SourceMask>? masks = null)
    {
        var configuration = new EfFluentConfiguration();
        var seenRelationships = new HashSet<string>(StringComparer.Ordinal);
        var classesByFull = files
            .SelectMany(f => f.Scan.Classes)
            .GroupBy(c => c.FullName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var (file, scan) in files)
        {
            SourceMask? mask = null;
            foreach (var declaration in scan.Classes)
            {
                var isContext = CSharpScanner.IsDbContextBase(declaration.BaseList);
                var configTarget = FluentTypeResolver.ResolveConfigurationTarget(declaration, hierarchy);
                if (!isContext && configTarget is null) continue;

                mask ??= FluentSourceReader.ResolveMask(file, masks);
                var body = FluentSourceReader.FindMethodBody(
                    file.Code,
                    mask,
                    declaration.BodySpan,
                    isContext ? "OnModelCreating" : "Configure");
                if (body is null) continue;

                foreach (var statement in FluentSourceReader.SplitStatements(
                             file.Code, mask, body.Value.Start, body.Value.End))
                {
                    var parsed = FluentStatementParser.Parse(
                        statement,
                        declaration,
                        configTarget,
                        hierarchy,
                        classesByFull);

                    configuration.Keys.AddRange(parsed.Keys);
                    if (parsed.Relationship is { } relationship &&
                        seenRelationships.Add(FluentRelationshipBuilder.DedupeKey(relationship)))
                    {
                        configuration.Relationships.Add(relationship);
                    }
                }
            }
        }

        return configuration;
    }
}
