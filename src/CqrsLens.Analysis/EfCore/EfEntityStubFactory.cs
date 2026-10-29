namespace CqrsLens;

/// <summary>Creates placeholder entities for DbSet types that cannot be resolved in source.</summary>
internal static class EfEntityStubFactory
{
    internal static EntityInfo? Create(string writtenType, DbContextInfo context)
    {
        var (name, args) = CSharpScanner.ParseTypeReference(EfAnnotations.NormalizeType(writtenType));
        if (args.Count > 0) return null;
        var simple = CSharpScanner.SimpleName(name.TrimEnd('?'));
        if (simple.Length == 0) return null;
        return new EntityInfo
        {
            Name = simple,
            Namespace = CSharpScanner.NamespaceOfType(name),
            ProjectName = context.ProjectName,
            Location = context.Location,
        };
    }
}
