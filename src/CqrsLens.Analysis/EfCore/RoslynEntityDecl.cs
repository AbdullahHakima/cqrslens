using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CqrsLens;

/// <summary>A class declaration indexed from Roslyn with the properties relevant to EF Core.</summary>
internal sealed class RoslynEntityDecl
{
    public required INamedTypeSymbol Symbol { get; init; }
    public required TypeDeclarationSyntax Syntax { get; init; }
    public required SemanticModel Model { get; init; }
    public required SourceFile File { get; init; }
    public required string FullName { get; init; }
    public List<RoslynPropertyDecl> Properties { get; } = new();
}
