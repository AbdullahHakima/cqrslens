using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CqrsLens;

/// <summary>A property or primary-constructor parameter read from a Roslyn entity declaration.</summary>
internal sealed class RoslynPropertyDecl
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public required bool IsNullable { get; init; }
    public required bool IsPublic { get; init; }
    public required bool IsStatic { get; init; }
    public ITypeSymbol? ResolvedType { get; init; }
    public TypeSyntax? TypeSyntax { get; init; }
    public SourceLocation? Location { get; init; }
    public List<string> Attributes { get; } = new();
}
