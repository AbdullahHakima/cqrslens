using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CqrsLens;

/// <summary>
/// Roslyn backend for shape discovery. This class only orchestrates the scan lifecycle:
/// parse/compile the workspace, walk each project's syntax trees, delegate shape extraction to
/// the focused readers, then hand the compilation to the EF model builder.
/// </summary>
internal sealed class RoslynArchitectureScanner : IArchitectureScanner
{
    public void Scan(
        SourceWorkspace workspace,
        IReadOnlySet<string> markers,
        IReadOnlySet<string> handlerMarkers,
        AnalysisResult result)
    {
        var allFiles = workspace.Files.ToList();
        var ordered = workspace.SuccessfullyParsed
            .OrderBy(f => f.File.Path, StringComparer.OrdinalIgnoreCase)
            .Select(f => (File: f.File, Scan: f.Scan!))
            .ToList();

        var injectedByPath = TextArchitectureScanner.ApplyProjectGlobalUsings(ordered, result.Projects);
        var hierarchy = new TypeHierarchy(
            ordered.SelectMany(o => o.Scan.Classes),
            ordered.SelectMany(o => o.Scan.InterfaceDecls));
        _ = TextArchitectureScanner.BuildInterfaceMap(ordered, hierarchy, result);
        var scansByPath = ordered.ToDictionary(
            o => o.File.Path,
            o => o.Scan,
            StringComparer.OrdinalIgnoreCase);

        var handlerRoots = TextArchitectureScanner.BuildHandlerRoots(handlerMarkers);

        List<(SourceFile File, SyntaxTree Tree)>? parsed = null;
        Compilation? compilation = null;

        foreach (var project in allFiles.GroupBy(
                     f => f.ProjectName ?? "",
                     StringComparer.OrdinalIgnoreCase))
        {
            var ownFiles = project.ToList();
            var projectLabel = RoslynSymbolFacts.ProjectLabel(project.Key);
            try
            {
                parsed ??= allFiles
                    .Select(file => (
                        File: file,
                        Tree: RoslynCompilationBuilder.ParseSourceFile(file, injectedByPath)))
                    .ToList();
                compilation ??= RoslynCompilationBuilder.CreateCompilation(parsed.Select(p => p.Tree));

                ScanProject(
                    ownFiles,
                    compilation,
                    parsed,
                    scansByPath,
                    hierarchy,
                    markers,
                    handlerMarkers,
                    handlerRoots,
                    result,
                    projectLabel);
            }
            catch (Exception ex)
            {
                foreach (var file in ownFiles)
                    result.RecordSkippedFile(file.Path);
                result.RecordDiagnostic(new AnalysisDiagnostic(
                    AnalysisResult.RoslynScanFailureCode,
                    AnalysisDiagnosticSeverity.Error,
                    $"Roslyn could not scan project `{projectLabel}`: {ex.Message}",
                    ownFiles.Count > 0
                        ? SourceLocation.FromLine(ownFiles[0].Path, result.RootPath, 1, 1)
                        : null,
                    projectLabel));
            }
        }

        if (compilation is not null && parsed is not null && result.DbContexts.Count > 0)
            EfCoreRoslynModelBuilder.Build(compilation, parsed, ordered, hierarchy, result);
    }

    private static void ScanProject(
        IReadOnlyList<SourceFile> ownFiles,
        Compilation compilation,
        IReadOnlyList<(SourceFile File, SyntaxTree Tree)> parsed,
        IReadOnlyDictionary<string, CSharpScanner.FileScan> scansByPath,
        TypeHierarchy hierarchy,
        IReadOnlySet<string> markers,
        IReadOnlySet<string> handlerMarkers,
        IReadOnlySet<string> handlerRoots,
        AnalysisResult result,
        string projectLabel)
    {
        var ownPaths = ownFiles
            .Select(f => f.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        RoslynContractDiagnostics.WarnAboutUnresolvedContracts(
            compilation, parsed, ownPaths, projectLabel, result);

        foreach (var (file, tree) in parsed.Where(p => ownPaths.Contains(p.File.Path)))
        {
            try
            {
                scansByPath.TryGetValue(file.Path, out var fileScan);
                ProcessTree(
                    compilation,
                    tree,
                    file,
                    fileScan,
                    hierarchy,
                    markers,
                    handlerMarkers,
                    handlerRoots,
                    result);
            }
            catch (Exception ex)
            {
                result.RecordSkippedFile(file.Path);
                result.RecordDiagnostic(new AnalysisDiagnostic(
                    AnalysisResult.RoslynScanFailureCode,
                    AnalysisDiagnosticSeverity.Error,
                    $"Roslyn could not scan `{file.Path}`: {ex.Message}",
                    SourceLocation.FromLine(file.Path, result.RootPath, 1, 1),
                    file.ProjectName));
            }
        }
    }

    private static void ProcessTree(
        Compilation compilation,
        SyntaxTree tree,
        SourceFile file,
        CSharpScanner.FileScan? fileScan,
        TypeHierarchy hierarchy,
        IReadOnlySet<string> markers,
        IReadOnlySet<string> handlerMarkers,
        IReadOnlySet<string> handlerRoots,
        AnalysisResult result)
    {
        var model = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
        var declarations = tree.GetRoot()
            .DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .Where(d => d is not InterfaceDeclarationSyntax);

        foreach (var declaration in declarations)
        {
            if (model.GetDeclaredSymbol(declaration) is not INamedTypeSymbol symbol)
                continue;

            var ns = RoslynSymbolFacts.NamespaceOf(symbol);
            var containingTypes = RoslynSymbolFacts.ContainingTypeNames(symbol);
            var classDecl = fileScan?.Classes.FirstOrDefault(c =>
                c.Name == symbol.Name &&
                c.Namespace == ns &&
                c.ContainingTypes.SequenceEqual(containingTypes));
            var usings = classDecl?.Usings ?? fileScan?.Usings;

            RoslynCqrsShapeReader.AddRequest(
                symbol, declaration, model, classDecl, file.ProjectName, markers, hierarchy, result);
            RoslynCqrsShapeReader.AddHandlers(
                symbol, declaration, model, usings, file.ProjectName, handlerMarkers, hierarchy, result);
            RoslynCqrsShapeReader.AddNotification(
                symbol, declaration, model, classDecl, file.ProjectName, hierarchy, result);
            RoslynCqrsShapeReader.AddNotificationHandlers(
                symbol, declaration, model, usings, file.ProjectName, hierarchy, result);
            RoslynCqrsShapeReader.AddValidators(
                symbol, declaration, model, usings, file.ProjectName, result);
            RoslynCqrsShapeReader.AddBehavior(
                symbol, declaration, model, file.ProjectName, result);
            RoslynEfShapeReader.AddDbContext(
                symbol, declaration, file.ProjectName, result);

            if (!symbol.IsAbstract &&
                classDecl is not null &&
                RoslynSymbolFacts.HasErrorBaseType(symbol, declaration, model) &&
                !RoslynContractDiagnostics.HasUnresolvedBuiltInContract(declaration, model))
            {
                RoslynContractDiagnostics.RecordUnboundContractDiagnostics(
                    file,
                    classDecl,
                    hierarchy,
                    markers,
                    handlerRoots,
                    result);
            }
        }
    }
}
