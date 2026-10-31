using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CqrsLens;

/// <summary>
/// Reads validator shapes and the request type each validator targets.
/// </summary>
internal static class RoslynValidatorShapeReader
{
    internal static void AddValidators(
        INamedTypeSymbol symbol,
        TypeDeclarationSyntax declaration,
        SemanticModel model,
        IReadOnlyList<UsingDirective>? usings,
        string? projectName,
        AnalysisResult result)
    {
        if (symbol.IsAbstract) return;

        var targets = new List<ITypeSymbol>();

        var validatorBase = RoslynSymbolFacts.FindBaseType(symbol, "AbstractValidator");
        if (validatorBase is { TypeArguments.Length: >= 1 })
            targets.Add(validatorBase.TypeArguments[0]);

        targets.AddRange(RoslynSymbolFacts.ImplementedInterfaces(symbol, declaration, model)
            .Where(i => i.Name == "IValidator" && i.TypeArguments.Length >= 1)
            .Select(i => i.TypeArguments[0]));

        foreach (var target in targets
                     .Where(t => t.TypeKind != TypeKind.TypeParameter)
                     .GroupBy(t => t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                     .Select(g => g.First()))
        {
            var targetName = RoslynSymbolFacts.TypeName(target);
            var targetNamespace = RoslynSymbolFacts.NamespaceOf(target);
            var normalizedNamespace = string.IsNullOrEmpty(targetNamespace) ? null : targetNamespace;
            var fullName = RoslynSymbolFacts.FullName(symbol);

            if (result.Validators.Any(v =>
                    v.FullName == fullName &&
                    v.ProjectName == projectName &&
                    v.TargetType == targetName &&
                    v.TargetNamespace == normalizedNamespace))
                continue;

            var validator = new ValidatorInfo
            {
                Name = symbol.Name,
                Namespace = RoslynSymbolFacts.NamespaceOf(symbol),
                TargetType = targetName,
                TargetNamespace = normalizedNamespace,
                ProjectName = projectName,
                Location = RoslynSymbolFacts.LocationOf(declaration.Identifier.GetLocation(), result.RootPath),
            };
            validator.ContainingTypes.AddRange(RoslynSymbolFacts.ContainingTypeNames(symbol));
            if (usings is not null)
                CSharpScanner.AddImportedNamespaces(usings, validator.ImportedNamespaces);
            result.Validators.Add(validator);
        }
    }
}
