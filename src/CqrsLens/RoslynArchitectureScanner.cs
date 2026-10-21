using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CqrsLens;

// First Roslyn slice for #1. It emits CQRS request/handler/validator shapes
// from source symbols and is intentionally not wired as the default scanner.
internal sealed class RoslynArchitectureScanner : IArchitectureScanner
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);

    private static readonly Lazy<IReadOnlyList<MetadataReference>> RuntimeReferences =
        new(CreateRuntimeReferences);

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
        AnalysisResult result)
    {
        var allFiles = files.ToList();
        foreach (var project in allFiles.GroupBy(
                     f => f.ProjectName ?? "",
                     StringComparer.OrdinalIgnoreCase))
        {
            var ownFiles = project.ToList();
            try
            {
                ScanProject(ownFiles, allFiles, markers, result);
            }
            catch (Exception ex)
            {
                foreach (var file in ownFiles)
                    result.RecordSkippedFile(file.Path);
                result.Checks.Add(new CheckFinding(
                    CheckSeverity.Warning,
                    "RoslynScanFailure",
                    $"Roslyn could not scan project `{ProjectLabel(project.Key)}`: {ex.Message}"));
            }
        }
    }

    private static void ScanProject(
        IReadOnlyList<SourceFile> ownFiles,
        IReadOnlyList<SourceFile> allFiles,
        IReadOnlySet<string> markers,
        AnalysisResult result)
    {
        var parsed = allFiles
            .Select(file => (
                File: file,
                Tree: CSharpSyntaxTree.ParseText(
                    file.Code,
                    ParseOptions,
                    file.Path)))
            .ToList();

        var compilation = CreateCompilation(parsed.Select(p => p.Tree));
        var ownPaths = ownFiles
            .Select(f => f.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (file, tree) in parsed.Where(p => ownPaths.Contains(p.File.Path)))
        {
            try
            {
                ProcessTree(compilation, tree, file, markers, result);
            }
            catch (Exception ex)
            {
                result.RecordSkippedFile(file.Path);
                result.Checks.Add(new CheckFinding(
                    CheckSeverity.Warning,
                    "RoslynScanFailure",
                    $"Roslyn could not scan `{file.Path}`: {ex.Message}"));
            }
        }
    }

    private static void ProcessTree(
        Compilation compilation,
        SyntaxTree tree,
        SourceFile file,
        IReadOnlySet<string> markers,
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

            AddRequest(symbol, file.ProjectName, markers, result);
            AddHandlers(symbol, file.ProjectName, result);
            AddValidators(symbol, file.ProjectName, result);
            AddBehavior(symbol, file.ProjectName, result);
            AddDbContext(symbol, file.ProjectName, result);
        }
    }

    private static void AddRequest(
        INamedTypeSymbol symbol,
        string? projectName,
        IReadOnlySet<string> markers,
        AnalysisResult result)
    {
        if (symbol.IsAbstract) return;

        var interfaces = ImplementedInterfaces(symbol).ToList();
        var requestInterface = interfaces.FirstOrDefault(i =>
            i.Name == "IRequest" && i.TypeArguments.Length > 0);
        requestInterface ??= interfaces.FirstOrDefault(i => i.Name == "IRequest");
        var markerInterface = interfaces.FirstOrDefault(i => markers.Contains(i.Name));
        if (requestInterface is null && markerInterface is null)
            return;

        var ns = NamespaceOf(symbol);
        if (result.Requests.Any(r =>
                r.Name == symbol.Name &&
                r.Namespace == ns &&
                r.ContainingTypes.SequenceEqual(ContainingTypeNames(symbol))))
            return;

        var viaMarker = markerInterface?.Name;
        if (viaMarker is null && requestInterface is not null)
        {
            viaMarker = symbol.Interfaces
                .FirstOrDefault(i =>
                    i.Name != "IRequest" &&
                    ImplementedInterfaces(i).Any(x => x.Name == "IRequest"))
                ?.Name;
        }

        var response = requestInterface?.TypeArguments.FirstOrDefault();
        if (response is null)
        {
            response = symbol.Interfaces
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
        };
        request.ContainingTypes.AddRange(ContainingTypeNames(symbol));

        if (!request.Name.EndsWith("Command", StringComparison.Ordinal) &&
            !request.Name.EndsWith("Query", StringComparison.Ordinal))
        {
            request.Kind = MarkerResolver.ClassifyMarkerRequest(
                request.Name,
                symbol.Interfaces.Select(i => i.Name)
                    .Concat(markerInterface is null ? [] : new[] { markerInterface.Name }));
        }

        result.Requests.Add(request);
    }

    private static void AddHandlers(
        INamedTypeSymbol symbol,
        string? projectName,
        AnalysisResult result)
    {
        if (symbol.IsAbstract) return;

        foreach (var handlerInterface in ImplementedInterfaces(symbol)
                     .Where(i => i.Name == "IRequestHandler" && i.TypeArguments.Length >= 1))
        {
            var target = handlerInterface.TypeArguments[0];
            if (target.TypeKind == TypeKind.TypeParameter)
                continue;

            var targetName = TypeName(target);
            var targetNamespace = NamespaceOf(target);
            var fullName = FullName(symbol);

            if (result.Handlers.Any(h =>
                    h.FullName == fullName &&
                    h.RequestName == targetName &&
                    h.RequestNamespace == targetNamespace))
                continue;

            var handler = new HandlerInfo
            {
                Name = symbol.Name,
                Namespace = NamespaceOf(symbol),
                RequestName = targetName,
                RequestNamespace = string.IsNullOrEmpty(targetNamespace) ? null : targetNamespace,
                ProjectName = projectName,
            };
            handler.ContainingTypes.AddRange(ContainingTypeNames(symbol));
            result.Handlers.Add(handler);
        }
    }

    private static void AddValidators(
        INamedTypeSymbol symbol,
        string? projectName,
        AnalysisResult result)
    {
        if (symbol.IsAbstract) return;

        var targets = new List<ITypeSymbol>();

        var validatorBase = FindBaseType(symbol, "AbstractValidator");
        if (validatorBase is { TypeArguments.Length: >= 1 })
            targets.Add(validatorBase.TypeArguments[0]);

        targets.AddRange(ImplementedInterfaces(symbol)
            .Where(i => i.Name == "IValidator" && i.TypeArguments.Length >= 1)
            .Select(i => i.TypeArguments[0]));

        foreach (var target in targets
                     .Where(t => t.TypeKind != TypeKind.TypeParameter)
                     .GroupBy(t => t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                     .Select(g => g.First()))
        {
            var targetName = TypeName(target);
            var targetNamespace = NamespaceOf(target);
            var fullName = FullName(symbol);

            if (result.Validators.Any(v =>
                    v.FullName == fullName &&
                    v.TargetType == targetName &&
                    v.TargetNamespace == targetNamespace))
                continue;

            var validator = new ValidatorInfo
            {
                Name = symbol.Name,
                Namespace = NamespaceOf(symbol),
                TargetType = targetName,
                TargetNamespace = string.IsNullOrEmpty(targetNamespace) ? null : targetNamespace,
                ProjectName = projectName,
            };
            validator.ContainingTypes.AddRange(ContainingTypeNames(symbol));
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

        var fluent = new List<string>();
        if (probe.GetTypeByMetadataName("FluentValidation.IValidator") is null)
            fluent.Add("public interface IValidator { }");
        if (probe.GetTypeByMetadataName("FluentValidation.IValidator`1") is null)
            fluent.Add("public interface IValidator<in T> : IValidator { }");
        if (probe.GetTypeByMetadataName("FluentValidation.AbstractValidator`1") is null)
            fluent.Add("public abstract class AbstractValidator<T> : IValidator<T> { protected AbstractValidator() { } }");
        AppendNamespace(sb, "FluentValidation", fluent);

        var ef = new List<string>();
        if (probe.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbContext") is null)
            ef.Add("public class DbContext { protected DbContext() { } protected DbContext(object? options) { } }");
        if (probe.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbSet`1") is null)
            ef.Add("public abstract class DbSet<TEntity> where TEntity : class { }");
        if (probe.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbContextOptions`1") is null)
            ef.Add("public class DbContextOptions<TContext> where TContext : DbContext { }");
        AppendNamespace(sb, "Microsoft.EntityFrameworkCore", ef);

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
        string? projectName,
        AnalysisResult result)
    {
        if (!ImplementedInterfaces(symbol).Any(i =>
                i.Name == "IPipelineBehavior" &&
                i.TypeArguments.Length == 2))
            return;

        if (result.Behaviors.Any(b => b.Name == symbol.Name && b.ProjectName == projectName))
            return;

        result.Behaviors.Add(new BehaviorInfo
        {
            Name = symbol.Name,
            ProjectName = projectName,
        });
    }

    private static void AddDbContext(
        INamedTypeSymbol symbol,
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

    private static IEnumerable<INamedTypeSymbol> ImplementedInterfaces(INamedTypeSymbol symbol)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in symbol.Interfaces.Concat(symbol.AllInterfaces))
        {
            var key = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (seen.Add(key))
                yield return type;
        }
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

    private static string NamespaceOf(INamedTypeSymbol symbol)
        => symbol.ContainingNamespace is { IsGlobalNamespace: false } ns
            ? ns.ToDisplayString()
            : "";

    private static string ProjectLabel(string project)
        => string.IsNullOrEmpty(project) ? "(no project)" : project;

    private static string FullName(INamedTypeSymbol symbol)
    {
        var typeNames = ContainingTypeNames(symbol).Concat(new[] { symbol.Name });
        var path = string.Join(".", typeNames);
        var ns = NamespaceOf(symbol);
        return string.IsNullOrEmpty(ns) ? path : $"{ns}.{path}";
    }

    private static List<string> ContainingTypeNames(INamedTypeSymbol symbol)
    {
        var names = new List<string>();
        for (var current = symbol.ContainingType; current is not null; current = current.ContainingType)
            names.Insert(0, current.Name);
        return names;
    }

    private static string DisplayType(ITypeSymbol symbol)
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
