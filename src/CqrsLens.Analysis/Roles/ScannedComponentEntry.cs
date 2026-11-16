namespace CqrsLens;

/// <summary>
/// Aggregated source declaration entry for a single <see cref="ComponentId"/>,
/// folding partial-class declarations into one identity and tracking combined modifiers.
/// </summary>
internal sealed class ScannedComponentEntry
{
    private readonly List<ScannedDeclarationPart> _declarations = new();

    public ComponentId Id { get; }
    public string FullName { get; }
    public string SimpleName { get; }
    public int GenericArity { get; }
    public string? ProjectName { get; }
    public bool IsTestProject { get; }
    public bool IsAbstract { get; private set; }
    public bool IsStatic { get; private set; }
    public bool IsConcrete => !IsAbstract && !IsStatic;
    public SourceLocation? PrimaryLocation { get; private set; }
    public IReadOnlyList<ScannedDeclarationPart> Declarations => _declarations;

    public ScannedComponentEntry(
        ComponentId id,
        string fullName,
        string simpleName,
        int genericArity,
        string? projectName,
        bool isTestProject)
    {
        Id = id;
        FullName = fullName;
        SimpleName = simpleName;
        GenericArity = genericArity;
        ProjectName = projectName;
        IsTestProject = isTestProject;
    }

    /// <summary>Adds a class declaration part for this component symbol.</summary>
    public void AddDeclaration(
        CSharpScanner.ClassDecl declaration,
        SourceFile file,
        SourceMask mask,
        SourceLocation? location)
    {
        _declarations.Add(new ScannedDeclarationPart(declaration, file, mask, location));
        PrimaryLocation ??= location;
        if (declaration.IsAbstract)
            IsAbstract = true;
        if (CSharpSyntax.IsStaticDeclaration(file.Code, declaration.DeclarationIndex))
            IsStatic = true;
    }

    internal sealed record ScannedDeclarationPart(
        CSharpScanner.ClassDecl Declaration,
        SourceFile File,
        SourceMask Mask,
        SourceLocation? Location);
}
