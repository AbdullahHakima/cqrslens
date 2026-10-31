using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CqrsLens;

/// <summary>Reads EF-relevant properties and primary-constructor parameters from Roslyn syntax.</summary>
internal static class RoslynEntityPropertyExtractor
{
    internal static void AddProperties(
        RoslynEntityDecl entry,
        TypeDeclarationSyntax syntax,
        SemanticModel model,
        string rootPath)
    {
        foreach (var propSyntax in syntax.Members.OfType<PropertyDeclarationSyntax>())
        {
            if (propSyntax.AccessorList is null)
                continue;

            var hasSetter = propSyntax.AccessorList.Accessors.Any(a =>
                a.Keyword.IsKind(SyntaxKind.SetKeyword) ||
                a.Keyword.IsKind(SyntaxKind.InitKeyword));
            if (!hasSetter)
                continue;

            if (propSyntax.Modifiers.Any(m =>
                    m.IsKind(SyntaxKind.StaticKeyword) ||
                    m.IsKind(SyntaxKind.ConstKeyword)))
                continue;

            var isPublic = propSyntax.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword));
            var propSymbol = model.GetDeclaredSymbol(propSyntax);
            var rawType = EfAnnotations.NormalizeType(propSyntax.Type.ToString());
            if (model.GetAliasInfo(propSyntax.Type) is not null && propSymbol is not null)
                rawType = EfAnnotations.NormalizeType(RoslynSymbolFacts.DisplayType(propSymbol.Type));

            var propDecl = new RoslynPropertyDecl
            {
                Name = propSyntax.Identifier.ValueText,
                Type = rawType,
                IsNullable = EfAnnotations.IsNullable(rawType),
                IsPublic = isPublic,
                IsStatic = false,
                ResolvedType = propSymbol?.Type,
                TypeSyntax = propSyntax.Type,
                Location = RoslynSymbolFacts.LocationOf(propSyntax.GetLocation(), rootPath),
            };
            foreach (var attribute in propSyntax.AttributeLists.SelectMany(al => al.Attributes))
                propDecl.Attributes.Add("[" + attribute + "]");

            entry.Properties.Add(propDecl);
        }

        if (syntax.ParameterList is null)
            return;

        foreach (var param in syntax.ParameterList.Parameters)
        {
            if (param.Type is null) continue;
            var name = param.Identifier.ValueText;
            if (name.Length == 0 || entry.Properties.Any(p => p.Name == name))
                continue;

            var rawType = EfAnnotations.NormalizeType(param.Type.ToString());
            var resolvedType = model.GetTypeInfo(param.Type).Type;
            if (model.GetAliasInfo(param.Type) is not null && resolvedType is not null)
                rawType = EfAnnotations.NormalizeType(RoslynSymbolFacts.DisplayType(resolvedType));

            var propDecl = new RoslynPropertyDecl
            {
                Name = name,
                Type = rawType,
                IsNullable = EfAnnotations.IsNullable(rawType),
                IsPublic = true,
                IsStatic = false,
                ResolvedType = resolvedType,
                TypeSyntax = param.Type,
                Location = RoslynSymbolFacts.LocationOf(syntax.GetLocation(), rootPath),
            };
            foreach (var attribute in param.AttributeLists.SelectMany(al => al.Attributes))
                propDecl.Attributes.Add("[" + attribute + "]");

            entry.Properties.Add(propDecl);
        }
    }
}
