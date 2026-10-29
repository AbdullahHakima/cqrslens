namespace CqrsLens;

/// <summary>
/// API-compatible model container for the text scanner. Behavior lives in
/// <see cref="SourceFileScanner"/> and <see cref="ShapeApplier"/>.
/// </summary>
public static partial class CSharpScanner
{
    /// <summary>Everything found in one source file.</summary>
    public sealed class FileScan
    {
        public required string Namespace { get; init; }
        public List<ClassDecl> Classes { get; } = new();
        public List<InterfaceDecl> InterfaceDecls { get; } = new();

        /// <summary>
        /// Local and global using directives in this file. Project-level global
        /// usings are appended by the text scanner before resolution runs.
        /// </summary>
        public List<UsingDirective> Usings { get; } = new();

        /// <summary>Interface name → its base interface names as written, generics stripped.</summary>
        public Dictionary<string, List<string>> Interfaces { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>A class/record/struct declaration and the shapes it implements.</summary>
    public sealed class ClassDecl
    {
        public required string Name { get; init; }
        public required string Namespace { get; init; }
        public required string BaseList { get; init; }

        /// <summary>All base type names from the declaration, generics stripped.</summary>
        public required List<string> BaseTypeNames { get; init; }

        /// <summary>Simple names for the same base types.</summary>
        public required List<string> BaseRawNames { get; init; }

        /// <summary>Declared generic parameter names (e.g. TRequest, TResponse).</summary>
        public List<string> TypeParameters { get; init; } = new();

        /// <summary>Using directives from the declaring file, shared with the scan.</summary>
        public List<UsingDirective> Usings { get; init; } = new();

        public bool IsAbstract { get; init; }

        public List<string> ContainingTypes { get; } = new();

        /// <summary>Span of the class body in the source text (for DbSet attribution).</summary>
        public required (int Start, int End) BodySpan { get; init; }

        /// <summary>Index of the declaration keyword in the source file.</summary>
        public int DeclarationIndex { get; init; }

        /// <summary>Index of the declared type identifier in the source file.</summary>
        public int NameIndex { get; init; }

        /// <summary>Primary-constructor parameter list, when the declaration has one.</summary>
        public string? PrimaryConstructorParameters { get; init; }

        /// <summary>Properties used by the EF Core ER model; parsed best-effort from source.</summary>
        public List<PropertyDecl> Properties { get; } = new();

        public string FullName
        {
            get
            {
                var typePath = ContainingTypes.Count == 0 ? Name : string.Join(".", ContainingTypes) + "." + Name;
                return string.IsNullOrEmpty(Namespace) ? typePath : $"{Namespace}.{typePath}";
            }
        }
    }

    /// <summary>
    /// A property or record primary-constructor parameter. Text mode uses this for
    /// EF Core entity discovery; it intentionally captures only shapes that map
    /// safely without a semantic model.
    /// </summary>
    public sealed class PropertyDecl
    {
        public required string Name { get; init; }
        public required string Type { get; init; }
        public bool IsNullable { get; init; }
        public bool HasSetter { get; init; }
        public bool IsPublic { get; init; }
        public bool IsStatic { get; init; }
        public bool IsPrimaryConstructorParameter { get; init; }
        public int SourceIndex { get; init; }

        /// <summary>Raw attribute text, including the surrounding brackets.</summary>
        public List<string> Attributes { get; } = new();
    }

    /// <summary>An interface declaration, including its raw base list for generic substitution.</summary>
    public sealed class InterfaceDecl
    {
        public required string Name { get; init; }
        public required string Namespace { get; init; }
        public required string BaseList { get; init; }
        public List<string> TypeParameters { get; init; } = new();

        /// <summary>Using directives from the declaring file, shared with the scan.</summary>
        public List<UsingDirective> Usings { get; init; } = new();

        public List<string> ContainingTypes { get; } = new();
        public required (int Start, int End) BodySpan { get; init; }

        public string FullName
        {
            get
            {
                var typePath = ContainingTypes.Count == 0 ? Name : string.Join(".", ContainingTypes) + "." + Name;
                return string.IsNullOrEmpty(Namespace) ? typePath : $"{Namespace}.{typePath}";
            }
        }
    }
}
