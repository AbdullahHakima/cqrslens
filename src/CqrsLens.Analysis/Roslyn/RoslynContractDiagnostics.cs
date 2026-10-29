using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CqrsLens;

/// <summary>
/// Detects contracts that Roslyn could not bind and records actionable diagnostics. This keeps
/// missing packages/usings from masquerading as a clean empty scan while also reporting shape
/// resolution gaps that text fallback had already identified.
/// </summary>
internal static class RoslynContractDiagnostics
{
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

    /// <summary>
    /// A missing using/package reference should not look like a clean empty scan.
    /// Contract shims only help when the compiler can see the namespace; if a known
    /// contract identifier still has no symbol, report it as a warning.
    /// </summary>
    internal static void WarnAboutUnresolvedContracts(
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
                firstLocation ??= RoslynSymbolFacts.LocationOf(name.GetLocation(), result.RootPath);
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

    internal static bool HasUnresolvedBuiltInContract(
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

    internal static void RecordUnboundContractDiagnostics(
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
            if (simple is "DbContext" or "IdentityDbContext" or "IPipelineBehavior" or
                          "INotification" or "IDomainEvent" or "INotificationHandler" or "IDomainEventHandler")
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
                    (handlerRoots.Contains(simple) || ContractRoots.LooksLikeHandlerContract(simple)))
                {
                    ContractDiagnostics.RecordShapeResolutionGap(
                        result, file, decl.BodySpan.Start, decl.Name, simple,
                        hierarchy, handlerAmbiguity, "handler");
                }
            }

            if (!isValidator && simple is not ("AbstractValidator" or "IValidator"))
            {
                var resolvedValidator = hierarchy.TryResolveShapeTarget(
                    decl.Namespace,
                    part,
                    ContractRoots.ValidatorRoots,
                    decl.Usings,
                    decl.ContainingTypes,
                    out _,
                    out var validatorAmbiguity);
                if (!resolvedValidator && ContractRoots.LooksLikeValidatorContract(simple))
                {
                    ContractDiagnostics.RecordShapeResolutionGap(
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
                    ContractDiagnostics.RecordAmbiguous(
                        result, file, decl.BodySpan.Start, decl.Name, name, ambiguous, "request marker");
                }
            }
        }

        if (!isRequest && !directAmbiguity)
        {
            var shape = hierarchy.ResolveRequestShape(decl, markers);
            if (shape.Roots.Count == 0)
            {
                ContractDiagnostics.RecordRequestResolutionGap(
                    result, file, decl.BodySpan.Start, decl.Name, shape);
            }
        }
    }
}
