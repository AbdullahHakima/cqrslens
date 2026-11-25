namespace CqrsLens;

/// <summary>
/// Finds concrete <see cref="DbContextInfo"/> declarations whose immediate base class
/// does not match <see cref="CSharpSyntax.IsDbContextBase"/>, by walking source-declared
/// base classes transitively via <see cref="TypeHierarchy"/>.
/// </summary>
internal static class TransitiveDbContextDiscoverer
{
    private const int MaxDepth = 32;

    public static void Discover(
        IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan)> ordered,
        TypeHierarchy hierarchy,
        AnalysisResult result,
        IReadOnlyDictionary<string, SourceMask>? masks = null)
    {
        var existing = new HashSet<string>(
            result.DbContexts.Select(c => KeyOf(c.Namespace, c.ContainingTypes, c.Name)),
            StringComparer.Ordinal);

        foreach (var (file, scan) in ordered)
        {
            var addedInFile = new List<(CSharpScanner.ClassDecl Decl, DbContextInfo Info)>();
            foreach (var decl in scan.Classes)
            {
                if (decl.IsAbstract || decl.BaseRawNames.Count == 0)
                    continue;

                var key = KeyOf(decl.Namespace, decl.ContainingTypes, decl.Name);
                if (existing.Contains(key))
                    continue;

                if (!DerivesFromDbContext(decl, hierarchy))
                    continue;

                var ctx = new DbContextInfo
                {
                    Name = decl.Name,
                    Namespace = decl.Namespace,
                    ProjectName = file.ProjectName,
                    Location = SourceLocation.Create(
                        file.Path, result.RootPath, file.Code, decl.NameIndex),
                };
                ctx.ContainingTypes.AddRange(decl.ContainingTypes);

                existing.Add(key);
                result.DbContexts.Add(ctx);
                addedInFile.Add((decl, ctx));
            }

            if (addedInFile.Count > 0)
            {
                var mask = masks is not null && masks.TryGetValue(file.Path, out var cached)
                    ? cached
                    : null;
                ShapeApplier.PopulateDbSets(file.Code, mask, addedInFile);
            }
        }
    }

    private static bool DerivesFromDbContext(
        CSharpScanner.ClassDecl start,
        TypeHierarchy hierarchy)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        return Visit(start, depth: 0);

        bool Visit(CSharpScanner.ClassDecl current, int depth)
        {
            if (depth > MaxDepth || !visited.Add(current.FullName))
                return false;

            if (CSharpSyntax.IsDbContextBase(current.BaseList))
                return true;

            foreach (var part in CSharpScanner.SplitTopLevel(current.BaseList))
            {
                var (name, args) = CSharpScanner.ParseTypeReference(part);
                var resolved = hierarchy.TryResolveClass(
                    name,
                    current.Namespace,
                    current.Usings,
                    current.ContainingTypes,
                    args.Count,
                    out _);
                if (resolved is not null && Visit(resolved, depth + 1))
                    return true;
            }

            return false;
        }
    }

    private static string KeyOf(
        string ns,
        IReadOnlyList<string> containingTypes,
        string name)
        => $"{ns}\u001f{string.Join(".", containingTypes)}\u001f{name}";
}
