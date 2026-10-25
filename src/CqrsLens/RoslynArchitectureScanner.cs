using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CqrsLens;

// Roslyn backend for #1. It emits CQRS request/handler/validator/behavior and
// EF Core shapes from source symbols while text mode remains the default.
internal sealed class RoslynArchitectureScanner : IArchitectureScanner
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);

    private static readonly Lazy<IReadOnlyList<MetadataReference>> RuntimeReferences =
        new(CreateRuntimeReferences);

    private static readonly HashSet<string> ContractNames = new(StringComparer.Ordinal)
    {
        "IRequest",
        "IRequestHandler",
        "IPipelineBehavior",
        "AbstractValidator",
        "IValidator",
        "Validator",
        "DbContext",
        "DbContextOptions",
        "DbSet",
        "IdentityDbContext",
    };

    private const string GlobalUsings = """
        global using System;
        global using System.Collections.Generic;
        global using System.Linq;
        global using System.Threading;
        global using System.Threading.Tasks;
        """;

    public void Scan(
        IReadOnlyList<SourceFile> files,
        IReadOnlySet<string> markers,
        IReadOnlySet<string> handlerMarkers,
        AnalysisResult result)
    {
        var allFiles = files.ToList();
        var ordered = new List<(SourceFile File, CSharpScanner.FileScan Scan)>(allFiles.Count);
        foreach (var file in allFiles.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                ordered.Add((file, CSharpScanner.ScanText(file.Code)));
            }
            catch
            {
                // Ignore text-scan fallback failure here; Roslyn will scan the file directly.
            }
        }

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
            try
            {
                parsed ??= allFiles
                    .Select(file => (
                        File: file,
                        Tree: ParseSourceFile(file, injectedByPath)))
                    .ToList();
                compilation ??= CreateCompilation(parsed.Select(p => p.Tree));

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
                    ProjectLabel(project.Key));
            }
            catch (Exception ex)
            {
                foreach (var file in ownFiles)
                    result.RecordSkippedFile(file.Path);
                result.RecordDiagnostic(new AnalysisDiagnostic(
                    AnalysisResult.RoslynScanFailureCode,
                    AnalysisDiagnosticSeverity.Error,
                    $"Roslyn could not scan project `{ProjectLabel(project.Key)}`: {ex.Message}",
                    ownFiles.Count > 0
                        ? SourceLocation.FromLine(ownFiles[0].Path, result.RootPath, 1, 1)
                        : null,
                    ProjectLabel(project.Key)));
            }
        }

        if (compilation is not null && parsed is not null && result.DbContexts.Count > 0)
            EfCoreRoslynModelBuilder.Build(compilation, parsed, ordered, hierarchy, result);
    }

    private static SyntaxTree ParseSourceFile(
        SourceFile file,
        IReadOnlyDictionary<string, List<UsingDirective>> injectedByPath)
    {
        var code = UsingDirectiveParser.StripGlobalUsingKeywords(file.Code);
        if (injectedByPath.TryGetValue(file.Path, out var injected) && injected.Count > 0)
        {
            if (code.Length > 0 && code[0] == '\uFEFF')
                code = code[1..];
            var preamble = string.Join("\n", injected.Select(u => u.ToUsingDeclaration())) + "\n#line 1\n";
            code = preamble + code;
        }

        return CSharpSyntaxTree.ParseText(code, ParseOptions, file.Path);
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

        WarnAboutUnresolvedContracts(compilation, parsed, ownPaths, projectLabel, result);

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

    /// <summary>
    /// A missing using/package reference should not look like a clean empty scan.
    /// Contract shims only help when the compiler can see the namespace; if a known
    /// contract identifier still has no symbol, report it as a warning.
    /// </summary>
    private static void WarnAboutUnresolvedContracts(
        Compilation compilation,
        IReadOnlyList<(SourceFile File, SyntaxTree Tree)> parsed,
        IReadOnlySet<string> ownPaths,
        string projectLabel,
        AnalysisResult result)
    {
        var unresolved = new SortedSet<string>(StringComparer.Ordinal);
        SourceLocation? firstLocation = null;

        foreach (var (file, tree) in parsed.Where(p => ownPaths.Contains(p.File.Path)))
        {
            var model = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
            foreach (var name in tree.GetRoot().DescendantNodes().OfType<SimpleNameSyntax>())
            {
                var text = name.Identifier.ValueText;
                if (!ContractNames.Contains(text))
                    continue;

                var info = model.GetSymbolInfo(name);
                if (info.Symbol is not null || info.CandidateSymbols.Length != 0)
                    continue;

                unresolved.Add(text);
                firstLocation ??= LocationOf(name.GetLocation(), result.RootPath);
            }
        }

        if (unresolved.Count == 0)
            return;

        result.RecordDiagnostic(new AnalysisDiagnostic(
            AnalysisResult.RoslynUnresolvedContractsCode,
            AnalysisDiagnosticSeverity.Warning,
            $"Roslyn could not bind {string.Join(", ", unresolved)} in project `{projectLabel}`. " +
            "Add the package/using or use text mode for a fallback scan.",
            firstLocation,
            projectLabel));
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

            var ns = NamespaceOf(symbol);
            var containingTypes = ContainingTypeNames(symbol);
            var classDecl = fileScan?.Classes.FirstOrDefault(c =>
                c.Name == symbol.Name &&
                c.Namespace == ns &&
                c.ContainingTypes.SequenceEqual(containingTypes));
            var usings = classDecl?.Usings ?? fileScan?.Usings;

            AddRequest(symbol, declaration, model, classDecl, file.ProjectName, markers, hierarchy, result);
            AddHandlers(symbol, declaration, model, usings, file.ProjectName, handlerMarkers, hierarchy, result);
            AddValidators(symbol, declaration, model, usings, file.ProjectName, result);
            AddBehavior(symbol, declaration, model, file.ProjectName, result);
            AddDbContext(symbol, declaration, file.ProjectName, result);

            if (!symbol.IsAbstract &&
                classDecl is not null &&
                HasErrorBaseType(symbol, declaration, model) &&
                !HasUnresolvedBuiltInContract(declaration, model))
            {
                RecordUnboundContractDiagnostics(
                    file,
                    classDecl,
                    hierarchy,
                    markers,
                    handlerRoots,
                    result);
            }
        }
    }

    private static void RecordUnboundContractDiagnostics(
        SourceFile file,
        CSharpScanner.ClassDecl decl,
        TypeHierarchy hierarchy,
        IReadOnlySet<string> markers,
        IReadOnlySet<string> handlerRoots,
        AnalysisResult result)
    {
        var isRequest = result.Requests.Any(r =>
            r.Name == decl.Name &&
            r.Namespace == decl.Namespace &&
            r.ContainingTypes.SequenceEqual(decl.ContainingTypes));
        var isHandler = result.Handlers.Any(h =>
            h.Name == decl.Name &&
            h.Namespace == decl.Namespace &&
            h.ContainingTypes.SequenceEqual(decl.ContainingTypes));
        var isValidator = result.Validators.Any(v =>
            v.Name == decl.Name &&
            v.Namespace == decl.Namespace &&
            v.ContainingTypes.SequenceEqual(decl.ContainingTypes));

        var directAmbiguity = false;
        foreach (var part in CSharpScanner.SplitTopLevel(decl.BaseList))
        {
            var (name, args) = CSharpScanner.ParseTypeReference(part);
            var simple = CSharpScanner.SimpleName(name);
            if (simple is "DbContext" or "IdentityDbContext" or "IPipelineBehavior")
                continue;

            if (!isHandler && simple != "IRequestHandler")
            {
                var resolvedHandler = hierarchy.TryResolveShapeTarget(
                    decl.Namespace,
                    part,
                    handlerRoots,
                    decl.Usings,
                    decl.ContainingTypes,
                    out _,
                    out var handlerAmbiguity);
                if (!resolvedHandler &&
                    (handlerRoots.Contains(simple) || TextArchitectureScanner.LooksLikeHandlerContract(simple)))
                {
                    TextArchitectureScanner.RecordShapeResolutionGap(
                        result, file, decl.BodySpan.Start, decl.Name, simple,
                        hierarchy, handlerAmbiguity, "handler");
                }
            }

            if (!isValidator && simple is not ("AbstractValidator" or "IValidator"))
            {
                var resolvedValidator = hierarchy.TryResolveShapeTarget(
                    decl.Namespace,
                    part,
                    TextArchitectureScanner.ValidatorRoots,
                    decl.Usings,
                    decl.ContainingTypes,
                    out _,
                    out var validatorAmbiguity);
                if (!resolvedValidator && TextArchitectureScanner.LooksLikeValidatorContract(simple))
                {
                    TextArchitectureScanner.RecordShapeResolutionGap(
                        result, file, decl.BodySpan.Start, decl.Name, simple,
                        hierarchy, validatorAmbiguity, "validator");
                }
            }

            if (!isRequest)
            {
                var resolved = hierarchy.ResolveInterfaceReference(
                    name,
                    decl.Namespace,
                    decl.Usings,
                    decl.ContainingTypes,
                    args.Count,
                    out var ambiguous);
                if (resolved is null && (markers.Contains(simple) || simple is "IRequest"))
                {
                    directAmbiguity = true;
                    TextArchitectureScanner.RecordAmbiguousContract(
                        result, file, decl.BodySpan.Start, decl.Name, name, ambiguous, "request marker");
                }
            }
        }

        if (!isRequest && !directAmbiguity)
        {
            var shape = hierarchy.ResolveRequestShape(decl, markers);
            if (shape.Roots.Count == 0)
            {
                TextArchitectureScanner.RecordRequestResolutionGap(
                    result, file, decl.BodySpan.Start, decl.Name, shape);
            }
        }
    }

    private static bool HasUnresolvedBuiltInContract(
        TypeDeclarationSyntax declaration,
        SemanticModel model)
    {
        if (declaration.BaseList is null)
            return false;

        foreach (var name in declaration.BaseList.DescendantNodes().OfType<SimpleNameSyntax>())
        {
            var text = name.Identifier.ValueText;
            if (!ContractNames.Contains(text))
                continue;

            var info = model.GetSymbolInfo(name);
            if (info.Symbol is null && info.CandidateSymbols.Length == 0)
                return true;
        }

        return false;
    }

    private static void AddRequest(
        INamedTypeSymbol symbol,
        TypeDeclarationSyntax declaration,
        SemanticModel model,
        CSharpScanner.ClassDecl? classDecl,
        string? projectName,
        IReadOnlySet<string> markers,
        TypeHierarchy hierarchy,
        AnalysisResult result)
    {
        if (symbol.IsAbstract) return;

        var interfaces = ImplementedInterfaces(symbol, declaration, model).ToList();
        var requestInterface = interfaces.FirstOrDefault(i =>
            i.Name == "IRequest" && i.TypeArguments.Length > 0);
        requestInterface ??= interfaces.FirstOrDefault(i => i.Name == "IRequest");
        var markerInterface = interfaces.FirstOrDefault(i => markers.Contains(i.Name));

        INamedTypeSymbol? externalMarker = null;
        if (requestInterface is null && markerInterface is null)
        {
            foreach (var errorBase in ErrorBaseTypes(declaration, model))
            {
                if (!markers.Contains(errorBase.Name))
                    continue;
                if (hierarchy.HasInterface(errorBase.Name))
                    continue;

                if (classDecl is not null)
                {
                    hierarchy.ResolveInterfaceReference(
                        errorBase.Name,
                        classDecl.Namespace,
                        classDecl.Usings,
                        classDecl.ContainingTypes,
                        errorBase.TypeArguments.Length,
                        out var ambiguous);
                    if (ambiguous.Count >= 2)
                        continue;
                }

                externalMarker = errorBase;
                break;
            }
        }

        if (requestInterface is null && markerInterface is null && externalMarker is null)
            return;

        var ns = NamespaceOf(symbol);
        if (result.Requests.Any(r =>
                r.Name == symbol.Name &&
                r.ProjectName == projectName &&
                r.Namespace == ns &&
                r.ContainingTypes.SequenceEqual(ContainingTypeNames(symbol))))
            return;

        var effectiveMarker = markerInterface ?? externalMarker;
        var viaMarker = effectiveMarker?.Name;
        if (viaMarker is null && requestInterface is not null)
        {
            viaMarker = interfaces
                .FirstOrDefault(i =>
                    i.Name != "IRequest" &&
                    ImplementedInterfaces(i).Any(x => x.Name == "IRequest"))
                ?.Name
                ?? BaseTypes(symbol)
                    .FirstOrDefault(b =>
                        b.Name != "Object" &&
                        ImplementedInterfaces(b).Any(x => x.Name == "IRequest"))
                    ?.Name;
        }

        var response = requestInterface?.TypeArguments.FirstOrDefault();
        if (response is null && effectiveMarker is { TypeArguments.Length: > 0 })
        {
            response = effectiveMarker.TypeArguments[0];
        }
        if (response is null)
        {
            response = interfaces
                .FirstOrDefault(i =>
                    i.TypeArguments.Length > 0 &&
                    (markers.Contains(i.Name) ||
                     ImplementedInterfaces(i).Any(x => markers.Contains(x.Name))))
                ?.TypeArguments[0];
        }

        var request = new CqrsRequest
        {
            Name = symbol.Name,
            Namespace = ns,
            ResponseType = response is null ? null : DisplayType(response),
            ProjectName = projectName,
            ViaMarker = viaMarker,
            Location = LocationOf(declaration.Identifier.GetLocation(), result.RootPath),
        };
        request.ContainingTypes.AddRange(ContainingTypeNames(symbol));

        if (viaMarker is not null &&
            !request.Name.EndsWith("Command", StringComparison.Ordinal) &&
            !request.Name.EndsWith("Query", StringComparison.Ordinal))
        {
            request.Kind = MarkerResolver.ClassifyMarkerRequest(
                request.Name,
                interfaces.Select(i => i.Name)
                    .Concat(BaseTypes(symbol).Select(b => b.Name))
                    .Concat(effectiveMarker is null ? [] : new[] { effectiveMarker.Name }));
        }

        result.Requests.Add(request);
    }

    private static void AddHandlers(
        INamedTypeSymbol symbol,
        TypeDeclarationSyntax declaration,
        SemanticModel model,
        IReadOnlyList<UsingDirective>? usings,
        string? projectName,
        IReadOnlySet<string> handlerMarkers,
        TypeHierarchy hierarchy,
        AnalysisResult result)
    {
        if (symbol.IsAbstract) return;

        var candidateInterfaces = ImplementedInterfaces(symbol, declaration, model)
            .Where(i => (i.Name == "IRequestHandler" || handlerMarkers.Contains(i.Name)) &&
                        i.TypeArguments.Length >= 1)
            .ToList();

        foreach (var errorBase in ErrorBaseTypes(declaration, model))
        {
            if (handlerMarkers.Contains(errorBase.Name) &&
                errorBase.TypeArguments.Length >= 1 &&
                !hierarchy.HasInterface(errorBase.Name))
            {
                candidateInterfaces.Add(errorBase);
            }
        }

        foreach (var handlerInterface in candidateInterfaces)
        {
            var target = handlerInterface.TypeArguments[0];
            if (target.TypeKind == TypeKind.TypeParameter)
                continue;

            var targetName = TypeName(target);
            var targetNamespace = NamespaceOf(target);
            var normalizedNamespace = string.IsNullOrEmpty(targetNamespace) ? null : targetNamespace;
            var fullName = FullName(symbol);

            if (result.Handlers.Any(h =>
                    h.FullName == fullName &&
                    h.ProjectName == projectName &&
                    h.RequestName == targetName &&
                    h.RequestNamespace == normalizedNamespace))
                continue;

            var handler = new HandlerInfo
            {
                Name = symbol.Name,
                Namespace = NamespaceOf(symbol),
                RequestName = targetName,
                RequestNamespace = normalizedNamespace,
                ProjectName = projectName,
                Location = LocationOf(declaration.Identifier.GetLocation(), result.RootPath),
            };
            handler.ContainingTypes.AddRange(ContainingTypeNames(symbol));
            if (usings is not null)
                CSharpScanner.AddImportedNamespaces(usings, handler.ImportedNamespaces);
            result.Handlers.Add(handler);
        }
    }

    private static void AddValidators(
        INamedTypeSymbol symbol,
        TypeDeclarationSyntax declaration,
        SemanticModel model,
        IReadOnlyList<UsingDirective>? usings,
        string? projectName,
        AnalysisResult result)
    {
        if (symbol.IsAbstract) return;

        var targets = new List<ITypeSymbol>();

        var validatorBase = FindBaseType(symbol, "AbstractValidator");
        if (validatorBase is { TypeArguments.Length: >= 1 })
            targets.Add(validatorBase.TypeArguments[0]);

        targets.AddRange(ImplementedInterfaces(symbol, declaration, model)
            .Where(i => i.Name == "IValidator" && i.TypeArguments.Length >= 1)
            .Select(i => i.TypeArguments[0]));

        foreach (var target in targets
                     .Where(t => t.TypeKind != TypeKind.TypeParameter)
                     .GroupBy(t => t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                     .Select(g => g.First()))
        {
            var targetName = TypeName(target);
            var targetNamespace = NamespaceOf(target);
            var normalizedNamespace = string.IsNullOrEmpty(targetNamespace) ? null : targetNamespace;
            var fullName = FullName(symbol);

            if (result.Validators.Any(v =>
                    v.FullName == fullName &&
                    v.ProjectName == projectName &&
                    v.TargetType == targetName &&
                    v.TargetNamespace == normalizedNamespace))
                continue;

            var validator = new ValidatorInfo
            {
                Name = symbol.Name,
                Namespace = NamespaceOf(symbol),
                TargetType = targetName,
                TargetNamespace = normalizedNamespace,
                ProjectName = projectName,
                Location = LocationOf(declaration.Identifier.GetLocation(), result.RootPath),
            };
            validator.ContainingTypes.AddRange(ContainingTypeNames(symbol));
            if (usings is not null)
                CSharpScanner.AddImportedNamespaces(usings, validator.ImportedNamespaces);
            result.Validators.Add(validator);
        }
    }

    private static Compilation CreateCompilation(IEnumerable<SyntaxTree> sourceTrees)
    {
        var trees = sourceTrees.ToList();
        var options = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            allowUnsafe: true);

        // Probe the real source first. If a project already declares one of the
        // contracts (unit-test shims or a vendored copy), never add a duplicate.
        var probe = CSharpCompilation.Create(
            "CqrsLens.RoslynScan.Probe",
            trees,
            RuntimeReferences.Value,
            options);

        var stubs = BuildStubSource(probe);
        if (stubs.Length == 0)
        {
            trees.Add(CSharpSyntaxTree.ParseText(GlobalUsings, ParseOptions, "CqrsLens.GlobalUsings.g.cs"));
            return CSharpCompilation.Create(
                "CqrsLens.RoslynScan",
                trees,
                RuntimeReferences.Value,
                options);
        }

        trees.Add(CSharpSyntaxTree.ParseText(stubs, ParseOptions, "CqrsLens.RoslynContracts.g.cs"));
        trees.Add(CSharpSyntaxTree.ParseText(GlobalUsings, ParseOptions, "CqrsLens.GlobalUsings.g.cs"));
        return CSharpCompilation.Create(
            "CqrsLens.RoslynScan",
            trees,
            RuntimeReferences.Value,
            options);
    }

    /// <summary>
    /// Real applications reference MediatR/FluentValidation/EF Core assemblies that
    /// are not part of the runtime reference set. The text scanner already matches
    /// these shapes by name; Roslyn needs symbols to do better. Add minimal contract
    /// shapes only for contracts the source does not define itself.
    /// </summary>
    private static string BuildStubSource(Compilation probe)
    {
        var sb = new StringBuilder();
        var mediatR = new List<string>();

        if (probe.GetTypeByMetadataName("MediatR.IRequest") is null)
            mediatR.Add("public interface IRequest { }");
        if (probe.GetTypeByMetadataName("MediatR.IRequest`1") is null)
            mediatR.Add("public interface IRequest<out TResponse> { }");
        if (probe.GetTypeByMetadataName("MediatR.IRequestHandler`1") is null)
            mediatR.Add("public interface IRequestHandler<in TRequest> where TRequest : IRequest { }");
        if (probe.GetTypeByMetadataName("MediatR.IRequestHandler`2") is null)
            mediatR.Add("public interface IRequestHandler<in TRequest, TResponse> where TRequest : IRequest<TResponse> { }");
        if (probe.GetTypeByMetadataName("MediatR.IPipelineBehavior`2") is null)
            mediatR.Add("public interface IPipelineBehavior<in TRequest, TResponse> where TRequest : notnull { }");
        if (probe.GetTypeByMetadataName("MediatR.RequestHandlerDelegate`1") is null)
            mediatR.Add("public delegate System.Threading.Tasks.Task<TResponse> RequestHandlerDelegate<TResponse>();");

        AppendNamespace(sb, "MediatR", mediatR);

        var mediator = new List<string>();
        if (probe.GetTypeByMetadataName("Mediator.IMessage") is null)
            mediator.Add("public interface IMessage { }");
        if (probe.GetTypeByMetadataName("Mediator.IStreamMessage") is null)
            mediator.Add("public interface IStreamMessage { }");
        if (probe.GetTypeByMetadataName("Mediator.IBaseRequest") is null)
            mediator.Add("public interface IBaseRequest : IMessage { }");
        if (probe.GetTypeByMetadataName("Mediator.IRequest") is null)
            mediator.Add("public interface IRequest : IBaseRequest { }");
        if (probe.GetTypeByMetadataName("Mediator.IRequest`1") is null)
            mediator.Add("public interface IRequest<out TResponse> : IBaseRequest { }");
        if (probe.GetTypeByMetadataName("Mediator.IBaseCommand") is null)
            mediator.Add("public interface IBaseCommand : IMessage { }");
        if (probe.GetTypeByMetadataName("Mediator.ICommand") is null)
            mediator.Add("public interface ICommand : IBaseCommand { }");
        if (probe.GetTypeByMetadataName("Mediator.ICommand`1") is null)
            mediator.Add("public interface ICommand<out TResponse> : IBaseCommand { }");
        if (probe.GetTypeByMetadataName("Mediator.IBaseQuery") is null)
            mediator.Add("public interface IBaseQuery : IMessage { }");
        if (probe.GetTypeByMetadataName("Mediator.IQuery`1") is null)
            mediator.Add("public interface IQuery<out TResponse> : IBaseQuery { }");
        if (probe.GetTypeByMetadataName("Mediator.IStreamRequest`1") is null)
            mediator.Add("public interface IStreamRequest<out TResponse> : IStreamMessage { }");
        if (probe.GetTypeByMetadataName("Mediator.IStreamCommand`1") is null)
            mediator.Add("public interface IStreamCommand<out TResponse> : IStreamMessage { }");
        if (probe.GetTypeByMetadataName("Mediator.IStreamQuery`1") is null)
            mediator.Add("public interface IStreamQuery<out TResponse> : IStreamMessage { }");
        if (probe.GetTypeByMetadataName("Mediator.IRequestHandler`1") is null)
            mediator.Add("public interface IRequestHandler<in TRequest> where TRequest : IRequest { }");
        if (probe.GetTypeByMetadataName("Mediator.IRequestHandler`2") is null)
            mediator.Add("public interface IRequestHandler<in TRequest, TResponse> where TRequest : IRequest<TResponse> { }");
        if (probe.GetTypeByMetadataName("Mediator.ICommandHandler`1") is null)
            mediator.Add("public interface ICommandHandler<in TCommand> where TCommand : ICommand { }");
        if (probe.GetTypeByMetadataName("Mediator.ICommandHandler`2") is null)
            mediator.Add("public interface ICommandHandler<in TCommand, TResponse> where TCommand : ICommand<TResponse> { }");
        if (probe.GetTypeByMetadataName("Mediator.IQueryHandler`2") is null)
            mediator.Add("public interface IQueryHandler<in TQuery, TResponse> where TQuery : IQuery<TResponse> { }");
        if (probe.GetTypeByMetadataName("Mediator.IStreamRequestHandler`2") is null)
            mediator.Add("public interface IStreamRequestHandler<in TRequest, TResponse> where TRequest : IStreamRequest<TResponse> { }");
        if (probe.GetTypeByMetadataName("Mediator.IStreamCommandHandler`2") is null)
            mediator.Add("public interface IStreamCommandHandler<in TCommand, TResponse> where TCommand : IStreamCommand<TResponse> { }");
        if (probe.GetTypeByMetadataName("Mediator.IStreamQueryHandler`2") is null)
            mediator.Add("public interface IStreamQueryHandler<in TQuery, TResponse> where TQuery : IStreamQuery<TResponse> { }");
        if (probe.GetTypeByMetadataName("Mediator.IPipelineBehavior`2") is null)
            mediator.Add("public interface IPipelineBehavior<TMessage, TResponse> where TMessage : notnull { }");
        if (probe.GetTypeByMetadataName("Mediator.IStreamPipelineBehavior`2") is null)
            mediator.Add("public interface IStreamPipelineBehavior<TMessage, TResponse> where TMessage : notnull { }");
        if (probe.GetTypeByMetadataName("Mediator.MessageHandlerDelegate`2") is null)
            mediator.Add("public delegate System.Threading.Tasks.ValueTask<TResponse> MessageHandlerDelegate<TMessage, TResponse>(TMessage message, System.Threading.CancellationToken cancellationToken);");
        AppendNamespace(sb, "Mediator", mediator);

        var fluent = new List<string>();
        if (probe.GetTypeByMetadataName("FluentValidation.IValidator") is null)
            fluent.Add("public interface IValidator { }");
        if (probe.GetTypeByMetadataName("FluentValidation.IValidator`1") is null)
            fluent.Add("public interface IValidator<in T> : IValidator { }");
        if (probe.GetTypeByMetadataName("FluentValidation.AbstractValidator`1") is null)
            fluent.Add("public abstract class AbstractValidator<T> : IValidator<T> { protected AbstractValidator() { } }");
        AppendNamespace(sb, "FluentValidation", fluent);

        var fastEndpoints = new List<string>();
        if (probe.GetTypeByMetadataName("FastEndpoints.Validator`1") is null)
            fastEndpoints.Add(
                "public abstract class Validator<T> : FluentValidation.AbstractValidator<T> { protected Validator() { } }");
        AppendNamespace(sb, "FastEndpoints", fastEndpoints);

        var ef = new List<string>();
        if (probe.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbContext") is null)
            ef.Add("public class DbContext { protected DbContext() { } protected DbContext(object? options) { } protected virtual void OnModelCreating(ModelBuilder modelBuilder) { } }");
        if (probe.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbSet`1") is null)
            ef.Add("public abstract class DbSet<TEntity> where TEntity : class { }");
        if (probe.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbContextOptions`1") is null)
            ef.Add("public class DbContextOptions<TContext> where TContext : DbContext { }");
        if (probe.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.ModelBuilder") is null)
            ef.Add("public class ModelBuilder { }");
        if (probe.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.IEntityTypeConfiguration`1") is null)
            ef.Add("public interface IEntityTypeConfiguration<TEntity> where TEntity : class { void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TEntity> builder); }");
        AppendNamespace(sb, "Microsoft.EntityFrameworkCore", ef);

        var efBuilders = new List<string>();
        if (probe.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder`1") is null)
            efBuilders.Add("public class EntityTypeBuilder<TEntity> where TEntity : class { }");
        AppendNamespace(sb, "Microsoft.EntityFrameworkCore.Metadata.Builders", efBuilders);

        var identity = new List<string>();
        if (probe.GetTypeByMetadataName("Microsoft.AspNetCore.Identity.EntityFrameworkCore.IdentityDbContext") is null)
            identity.Add("public class IdentityDbContext : Microsoft.EntityFrameworkCore.DbContext { }");
        if (probe.GetTypeByMetadataName("Microsoft.AspNetCore.Identity.EntityFrameworkCore.IdentityDbContext`1") is null)
            identity.Add("public class IdentityDbContext<TUser> : Microsoft.EntityFrameworkCore.DbContext where TUser : class { }");
        AppendNamespace(sb, "Microsoft.AspNetCore.Identity.EntityFrameworkCore", identity);

        return sb.ToString();
    }

    private static void AppendNamespace(StringBuilder sb, string ns, List<string> members)
    {
        if (members.Count == 0) return;
        sb.AppendLine($"namespace {ns}");
        sb.AppendLine("{");
        foreach (var member in members)
            sb.AppendLine("    " + member);
        sb.AppendLine("}");
    }

    private static void AddBehavior(
        INamedTypeSymbol symbol,
        TypeDeclarationSyntax declaration,
        SemanticModel model,
        string? projectName,
        AnalysisResult result)
    {
        if (!ImplementedInterfaces(symbol, declaration, model).Any(i =>
                i.Name == "IPipelineBehavior" &&
                i.TypeArguments.Length == 2))
            return;

        var location = LocationOf(declaration.Identifier.GetLocation(), result.RootPath);
        if (result.Behaviors.Any(b =>
                b.Name == symbol.Name &&
                b.ProjectName == projectName &&
                b.Location?.File == location?.File))
            return;

        result.Behaviors.Add(new BehaviorInfo
        {
            Name = symbol.Name,
            ProjectName = projectName,
            Location = location,
        });
    }

    private static void AddDbContext(
        INamedTypeSymbol symbol,
        TypeDeclarationSyntax declaration,
        string? projectName,
        AnalysisResult result)
    {
        if (symbol.IsAbstract || !IsDbContext(symbol))
            return;

        var ns = NamespaceOf(symbol);
        var containingTypes = ContainingTypeNames(symbol);
        if (result.DbContexts.Any(c =>
                c.Name == symbol.Name &&
                c.Namespace == ns &&
                c.ContainingTypes.SequenceEqual(containingTypes)))
            return;

        var context = new DbContextInfo
        {
            Name = symbol.Name,
            Namespace = ns,
            ProjectName = projectName,
            Location = LocationOf(declaration.Identifier.GetLocation(), result.RootPath),
        };
        context.ContainingTypes.AddRange(containingTypes);

        foreach (var property in symbol.GetMembers().OfType<IPropertySymbol>())
        {
            if (property.Type is not INamedTypeSymbol propertyType ||
                propertyType.Name != "DbSet" ||
                propertyType.TypeArguments.Length != 1)
                continue;

            context.DbSets.Add(new DbSetInfo(
                DisplayType(propertyType.TypeArguments[0]),
                property.Name));
        }

        result.DbContexts.Add(context);
    }

    private static bool IsDbContext(INamedTypeSymbol symbol)
    {
        for (var current = symbol.BaseType; current is not null; current = current.BaseType)
        {
            if (current.Name == "DbContext") return true;
            if (current.Name.EndsWith("DbContext", StringComparison.Ordinal) &&
                !current.Name.StartsWith('I'))
                return true;
        }
        return false;
    }

    private static IEnumerable<INamedTypeSymbol> ImplementedInterfaces(
        INamedTypeSymbol symbol,
        TypeDeclarationSyntax? declaration = null,
        SemanticModel? model = null)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        IEnumerable<INamedTypeSymbol> candidates;

        if (declaration is not null &&
            model is not null &&
            symbol.DeclaringSyntaxReferences.Length > 1 &&
            !declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            candidates = DeclarationInterfaces(declaration, model);
        }
        else
        {
            candidates = symbol.Interfaces.Concat(symbol.AllInterfaces);
        }

        foreach (var type in candidates)
        {
            var key = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (seen.Add(key))
                yield return type;
        }
    }

    private static IEnumerable<INamedTypeSymbol> DeclarationInterfaces(
        TypeDeclarationSyntax declaration,
        SemanticModel model)
    {
        if (declaration.BaseList is null)
            yield break;

        foreach (var baseSyntax in declaration.BaseList.Types)
        {
            if (model.GetTypeInfo(baseSyntax.Type).Type is not INamedTypeSymbol baseSymbol)
                continue;

            if (baseSymbol.TypeKind is TypeKind.Interface or TypeKind.Error)
                yield return baseSymbol;

            foreach (var transitive in baseSymbol.Interfaces.Concat(baseSymbol.AllInterfaces))
                yield return transitive;
        }
    }

    private static IEnumerable<INamedTypeSymbol> BaseTypes(INamedTypeSymbol symbol)
    {
        for (var current = symbol.BaseType; current is not null; current = current.BaseType)
            yield return current;
    }

    private static IEnumerable<INamedTypeSymbol> ErrorBaseTypes(
        TypeDeclarationSyntax declaration,
        SemanticModel model)
    {
        if (declaration.BaseList is null)
            yield break;

        foreach (var baseType in declaration.BaseList.Types)
        {
            if (model.GetTypeInfo(baseType.Type).Type is INamedTypeSymbol { TypeKind: TypeKind.Error } errorType)
                yield return errorType;
        }
    }

    private static bool HasErrorBaseType(
        INamedTypeSymbol symbol,
        TypeDeclarationSyntax declaration,
        SemanticModel model)
    {
        if (ErrorBaseTypes(declaration, model).Any())
            return true;

        for (var current = symbol.BaseType; current is not null; current = current.BaseType)
        {
            if (current.TypeKind == TypeKind.Error)
                return true;
        }

        return false;
    }

    private static INamedTypeSymbol? FindBaseType(INamedTypeSymbol symbol, string name)
    {
        for (var current = symbol.BaseType; current is not null; current = current.BaseType)
            if (current.Name == name)
                return current;
        return null;
    }

    private static string TypeName(ITypeSymbol symbol)
        => symbol is INamedTypeSymbol named
            ? named.Name
            : DisplayType(symbol);

    private static string NamespaceOf(ITypeSymbol symbol)
        => symbol is INamedTypeSymbol named ? NamespaceOf(named) : "";

    internal static string NamespaceOf(INamedTypeSymbol symbol)
        => symbol.ContainingNamespace is { IsGlobalNamespace: false } ns
            ? ns.ToDisplayString()
            : "";

    internal static SourceLocation? LocationOf(ISymbol symbol, string rootPath)
    {
        var location = symbol.Locations.FirstOrDefault(l => l.IsInSource);
        return location is null ? null : LocationOf(location, rootPath);
    }

    internal static SourceLocation? LocationOf(Location location, string rootPath)
    {
        var lineSpan = location.GetMappedLineSpan();
        return SourceLocation.FromLine(
            location.SourceTree?.FilePath,
            rootPath,
            lineSpan.StartLinePosition.Line + 1,
            lineSpan.StartLinePosition.Character + 1);
    }

    private static string ProjectLabel(string project)
        => string.IsNullOrEmpty(project) ? "(no project)" : project;

    internal static string FullName(INamedTypeSymbol symbol)
    {
        var typeNames = ContainingTypeNames(symbol).Concat(new[] { symbol.Name });
        var path = string.Join(".", typeNames);
        var ns = NamespaceOf(symbol);
        return string.IsNullOrEmpty(ns) ? path : $"{ns}.{path}";
    }

    internal static List<string> ContainingTypeNames(INamedTypeSymbol symbol)
    {
        var names = new List<string>();
        for (var current = symbol.ContainingType; current is not null; current = current.ContainingType)
            names.Insert(0, current.Name);
        return names;
    }

    internal static string DisplayType(ITypeSymbol symbol)
        => symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

    private static IReadOnlyList<MetadataReference> CreateRuntimeReferences()
    {
        var paths = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            ?.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            ?? [];

        return paths
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToArray();
    }
}
