namespace CqrsLens;

public sealed class ValidatorInfo
{
    public required string Name { get; init; }
    public string Namespace { get; set; } = "";
    public List<string> ContainingTypes { get; } = new();

    public string FullName =>
        TypeFullName.Compose(Namespace, ContainingTypes, Name);

    public required string TargetType { get; init; }

    // Set when the validated type name could be resolved to a namespace.
    public string? TargetNamespace { get; set; }

    /// <summary>Namespaces imported by the declaring file, used by the linker.</summary>
    internal List<string> ImportedNamespaces { get; } = new();

    public string? ProjectName { get; set; }
    public SourceLocation? Location { get; set; }
}
