using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CqrsLens;

/// <summary>
/// Indexes all class declarations in the Roslyn compilation alongside their cheap text
/// scans, so EF analysis can resolve entities by full or simple name.
/// </summary>
internal sealed class RoslynEntityIndex
{
    private readonly Dictionary<string, RoslynEntityDecl> _classes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CSharpScanner.ClassDecl> _textClasses = new(StringComparer.Ordinal);

    internal RoslynEntityIndex(
        Compilation compilation,
        IReadOnlyList<(SourceFile File, SyntaxTree Tree)> parsed,
        IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan)> textScans,
        string rootPath)
    {
        foreach (var (_, scan) in textScans)
        {
            foreach (var decl in scan.Classes)
                _textClasses.TryAdd(decl.FullName, decl);
        }

        foreach (var (file, tree) in parsed)
        {
            var model = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
            var declarations = tree.GetRoot()
                .DescendantNodes()
                .OfType<TypeDeclarationSyntax>()
                .Where(d => d is not InterfaceDeclarationSyntax);

            foreach (var syntax in declarations)
            {
                if (model.GetDeclaredSymbol(syntax) is not INamedTypeSymbol symbol)
                    continue;

                var fullName = RoslynSymbolFacts.FullName(symbol);
                if (!_classes.TryGetValue(fullName, out var entry))
                {
                    entry = new RoslynEntityDecl
                    {
                        Symbol = symbol,
                        Syntax = syntax,
                        Model = model,
                        File = file,
                        FullName = fullName,
                    };
                    _classes[fullName] = entry;
                }

                RoslynEntityPropertyExtractor.AddProperties(entry, syntax, model, rootPath);
            }
        }
    }

    internal bool TryGetClass(string fullName, out RoslynEntityDecl declaration)
        => _classes.TryGetValue(fullName, out declaration!);

    internal bool TryGetTextClass(string fullName, out CSharpScanner.ClassDecl declaration)
        => _textClasses.TryGetValue(fullName, out declaration!);
}
