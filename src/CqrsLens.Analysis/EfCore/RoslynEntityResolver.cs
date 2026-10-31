using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CqrsLens;

/// <summary>
/// Resolves EF entity declarations and navigation targets from Roslyn symbols, falling back
/// to the text hierarchy when the semantic model cannot bind a type.
/// </summary>
internal sealed class RoslynEntityResolver
{
    private readonly RoslynEntityIndex _index;
    private readonly TypeHierarchy _hierarchy;
    private readonly AnalysisResult _result;

    internal RoslynEntityResolver(RoslynEntityIndex index, TypeHierarchy hierarchy, AnalysisResult result)
    {
        _index = index;
        _hierarchy = hierarchy;
        _result = result;
    }

    internal RoslynEntityDecl? ResolveNavigationTarget(
        RoslynEntityDecl declaring,
        RoslynPropertyDecl property)
    {
        if (TryGetCollectionElement(property, out var elementWrittenType, out var elementSymbol))
        {
            return TryResolveEntityDecl(
                elementWrittenType, elementSymbol, null, declaring, recordAmbiguity: false, out var elementTarget)
                ? elementTarget
                : null;
        }

        return TryResolveEntityDecl(
            property.Type, property.ResolvedType, property.TypeSyntax, declaring, recordAmbiguity: false, out var target)
            ? target
            : null;
    }

    internal static bool TryGetCollectionElement(
        RoslynPropertyDecl property,
        out string elementWrittenType,
        out ITypeSymbol? elementSymbol)
    {
        elementSymbol = null;
        if (EfAnnotations.TryGetCollectionElementType(property.Type, out elementWrittenType))
        {
            var unwrapped = UnwrapNullable(property.ResolvedType);
            if (unwrapped is IArrayTypeSymbol arrayType)
                elementSymbol = arrayType.ElementType;
            else if (unwrapped is INamedTypeSymbol { TypeArguments.Length: 1 } named)
                elementSymbol = named.TypeArguments[0];
            return true;
        }

        return false;
    }

    internal RoslynEntityDecl? FirstBaseClass(RoslynEntityDecl declaration)
    {
        if (declaration.Symbol.BaseType is { TypeKind: not TypeKind.Error } baseSymbol)
        {
            var baseFullName = RoslynSymbolFacts.FullName(baseSymbol.OriginalDefinition);
            if (_index.TryGetClass(baseFullName, out var direct))
                return direct;
        }

        if (_index.TryGetTextClass(declaration.FullName, out var textDecl))
        {
            foreach (var part in CSharpScanner.SplitTopLevel(textDecl.BaseList))
            {
                var (name, args) = CSharpScanner.ParseTypeReference(part);
                if (name.Length == 0) continue;
                var resolved = _hierarchy.TryResolveClass(
                    name,
                    textDecl.Namespace,
                    textDecl.Usings,
                    textDecl.ContainingTypes,
                    args.Count,
                    out _);
                if (resolved is not null && _index.TryGetClass(resolved.FullName, out var entry))
                    return entry;
            }
        }

        return null;
    }

    internal bool TryResolveEntityDecl(
        string writtenType,
        ITypeSymbol? resolvedSymbol,
        TypeSyntax? typeSyntax,
        RoslynEntityDecl context,
        bool recordAmbiguity,
        out RoslynEntityDecl resolved)
    {
        resolved = null!;
        var unwrapped = UnwrapNullable(resolvedSymbol);
        if (unwrapped is INamedTypeSymbol { TypeKind: not TypeKind.Error } named)
        {
            var fullName = RoslynSymbolFacts.FullName(named.OriginalDefinition);
            if (_index.TryGetClass(fullName, out var direct))
            {
                resolved = direct;
                return true;
            }
        }

        var ambiguousCandidates = new List<string>();
        if (unwrapped is IErrorTypeSymbol { CandidateSymbols.Length: > 1 } errorType)
        {
            ambiguousCandidates.AddRange(errorType.CandidateSymbols
                .OfType<INamedTypeSymbol>()
                .Select(s => RoslynSymbolFacts.FullName(s.OriginalDefinition))
                .Where(IsKnownClass)
                .Distinct(StringComparer.Ordinal));
        }

        if (ambiguousCandidates.Count == 0 && typeSyntax is not null)
        {
            var info = context.Model.GetSymbolInfo(typeSyntax);
            if (info.CandidateSymbols.Length > 1)
            {
                ambiguousCandidates.AddRange(info.CandidateSymbols
                    .OfType<INamedTypeSymbol>()
                    .Select(s => RoslynSymbolFacts.FullName(s.OriginalDefinition))
                    .Where(IsKnownClass)
                    .Distinct(StringComparer.Ordinal));
            }
        }

        var normalized = EfAnnotations.NormalizeType(writtenType);
        if (normalized.EndsWith('?')) normalized = normalized[..^1];
        var (name, args) = CSharpScanner.ParseTypeReference(normalized);
        if (name.Length == 0) return false;

        if (_index.TryGetTextClass(context.FullName, out var textContext))
        {
            var candidate = _hierarchy.TryResolveClass(
                name,
                textContext.Namespace,
                textContext.Usings,
                textContext.ContainingTypes,
                args.Count,
                out var hierarchyAmbiguous);
            if (candidate is not null && _index.TryGetClass(candidate.FullName, out var fallback))
            {
                resolved = fallback;
                return true;
            }

            if (ambiguousCandidates.Count == 0 && hierarchyAmbiguous.Count > 0)
                ambiguousCandidates.AddRange(hierarchyAmbiguous);
        }

        if (recordAmbiguity && ambiguousCandidates.Count > 1)
        {
            _result.RecordDiagnostic(new AnalysisDiagnostic(
                AnalysisResult.AmbiguousLinkCode,
                AnalysisDiagnosticSeverity.Warning,
                $"EF Core type `{name}` matches more than one source class: " +
                string.Join(", ", ambiguousCandidates.OrderBy(a => a, StringComparer.Ordinal)) + ".",
                RoslynSymbolFacts.LocationOf(context.Symbol, _result.RootPath),
                context.File.ProjectName));
        }

        return false;
    }

    private bool IsKnownClass(string fullName)
        => _index.TryGetClass(fullName, out _);

    private static ITypeSymbol? UnwrapNullable(ITypeSymbol? symbol)
    {
        if (symbol is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T,
                TypeArguments.Length: 1
            } nullable)
        {
            return nullable.TypeArguments[0];
        }

        return symbol;
    }
}
