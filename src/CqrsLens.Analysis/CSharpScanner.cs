namespace CqrsLens;

/// <summary>
/// Public entry points for lightweight text-based C# scanning. The implementation is split by
/// responsibility across <see cref="SourceFileScanner"/> (file parsing), <see cref="ShapeApplier"/>
/// (shape classification) and <see cref="CSharpSyntax"/> (pure type-reference parsing). This
/// facade keeps the established API stable for callers and tests.
/// </summary>
public static partial class CSharpScanner
{
    /// <summary>Extracts the first namespace declared in a source file ("" when none).</summary>
    public static string ExtractNamespace(string code) => SourceFileScanner.ExtractNamespace(code);

    public static string SimpleName(string name) => CSharpSyntax.SimpleName(name);

    internal static string TypeIdentity(string fullName, int arity)
        => CSharpSyntax.TypeIdentity(fullName, arity);

    internal static string SimpleNameFromIdentity(string name)
        => CSharpSyntax.SimpleNameFromIdentity(name);

    public static string UnqualifiedTypeName(string name)
        => CSharpSyntax.UnqualifiedTypeName(name);

    public static string NamespaceOfType(string name)
        => CSharpSyntax.NamespaceOfType(name);

    public static string NamespaceOf(string name)
        => CSharpSyntax.NamespaceOf(name);

    public static List<string> ParseTypeParameters(string? parameters)
        => CSharpSyntax.ParseTypeParameters(parameters);

    public static List<string> ExtractBaseTypeNames(string baseList)
        => CSharpSyntax.ExtractBaseTypeNames(baseList);

    public static List<string> ExtractBaseRawNames(string baseList)
        => CSharpSyntax.ExtractBaseRawNames(baseList);

    public static (string Name, List<string> Arguments) ParseTypeReference(string typeReference)
        => CSharpSyntax.ParseTypeReference(typeReference);

    public static bool IsAbstractDeclaration(string code, int declarationIndex)
        => CSharpSyntax.IsAbstractDeclaration(code, declarationIndex);

    public static bool IsDbContextBase(string baseList)
        => CSharpSyntax.IsDbContextBase(baseList);

    public static List<string>? ExtractGenericArguments(string baseList, string interfaceName)
        => CSharpSyntax.ExtractGenericArguments(baseList, interfaceName);

    public static List<List<string>> ExtractAllGenericArguments(string baseList, string interfaceName)
        => CSharpSyntax.ExtractAllGenericArguments(baseList, interfaceName);

    public static List<string> SplitTopLevel(string inner)
        => CSharpSyntax.SplitTopLevel(inner);

    /// <summary>Scans one file's text into a fragment (pure; safe to run in parallel).</summary>
    public static FileScan ScanText(string code) => SourceFileScanner.ScanText(code);

    internal static FileScan ScanText(string code, SourceMask mask)
        => SourceFileScanner.ScanText(code, mask);

    internal static void AddImportedNamespaces(
        IEnumerable<UsingDirective> usings,
        List<string> target)
        => ShapeApplier.AddImportedNamespaces(usings, target);

    internal static void ApplyScan(
        FileScan scan,
        string code,
        string? projectName,
        AnalysisResult result,
        string? filePath = null,
        SourceMask? mask = null,
        ScanIdentityIndex? identities = null)
        => ShapeApplier.Apply(scan, code, projectName, result, filePath, mask, identities);

    /// <summary>
    /// Convenience for tests and single-file scans: scan text directly into a result.
    /// (v0.1-compatible entry point; files scanned this way get no marker resolution.)
    /// </summary>
    public static void ScanText(
        string code,
        string? projectName,
        AnalysisResult result,
        string? filePath = null)
    {
        var scan = ScanText(code);
        ApplyScan(scan, code, projectName, result, filePath);
        var file = new SourceFile(filePath ?? "", code, projectName);
        var hierarchy = new TypeHierarchy(scan.Classes, scan.InterfaceDecls);
        EfCoreTextModelBuilder.Build(new[] { (file, scan) }, hierarchy, result);
    }

    public static void ScanFile(string path, string? projectName, AnalysisResult result)
        => ScanText(File.ReadAllText(path), projectName, result, path);
}
