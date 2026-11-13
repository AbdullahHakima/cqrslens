namespace CqrsLens;

/// <summary>
/// The first consumer of the service-resolution index. For every handler
/// constructor dependency that is an interface whose resolved implementation is a
/// DbContext (the existing context model), record a resolved edge
/// (handler, via interface, context).
///
/// Fail closed: an interface the index cannot resolve, an ambiguous interface, or a
/// resolved implementation that is not a known DbContext simply produces no edge.
/// </summary>
internal static class HandlerDbContextResolver
{
    public static void Build(
        IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles,
        AnalysisResult result,
        TypeHierarchy? hierarchy = null)
    {
        if (result.ServiceResolutions.Count == 0 ||
            result.DbContexts.Count == 0 ||
            (result.Handlers.Count == 0 && result.NotificationHandlers.Count == 0))
            return;

        var contextsByFullName = new Dictionary<string, DbContextInfo>(StringComparer.Ordinal);
        foreach (var context in result.DbContexts)
            contextsByFullName.TryAdd(context.FullName, context);
        if (contextsByFullName.Count == 0) return;

        var resolutionBySignature = new Dictionary<string, ServiceResolution>(StringComparer.Ordinal);
        foreach (var resolution in result.ServiceResolutions)
            resolutionBySignature.TryAdd(resolution.Interface, resolution);

        // Handler declarations carry the usings/namespace needed to resolve a written
        // dependency; the index itself is keyed by resolved identities.
        var classes = new List<CSharpScanner.ClassDecl>();
        var interfaces = new List<CSharpScanner.InterfaceDecl>();
        var classByFullName = new Dictionary<string, CSharpScanner.ClassDecl>(StringComparer.Ordinal);
        foreach (var (_, scan, _) in parsedFiles)
        {
            foreach (var declaration in scan.Classes)
            {
                classes.Add(declaration);
                classByFullName.TryAdd(declaration.FullName, declaration);
            }
            interfaces.AddRange(scan.InterfaceDecls);
        }

        hierarchy ??= new TypeHierarchy(classes, interfaces);
        var edges = new List<ResolvedEdge>();

        foreach (var handler in EnumerateHandlers(result))
        {
            classByFullName.TryGetValue(handler.FullName, out var declaration);
            var currentNamespace = declaration?.Namespace ?? handler.Namespace;
            IReadOnlyList<UsingDirective> usings =
                declaration?.Usings ?? (IReadOnlyList<UsingDirective>)Array.Empty<UsingDirective>();
            IReadOnlyList<string> containingTypes =
                declaration?.ContainingTypes ?? (IReadOnlyList<string>)Array.Empty<string>();

            foreach (var dependency in handler.Dependencies)
            {
                var (name, arguments) = CSharpScanner.ParseTypeReference(dependency);
                if (name.Length == 0) continue;

                var identity = hierarchy.ResolveInterfaceReference(
                    name, currentNamespace, usings, containingTypes, arguments.Count, out _);
                if (identity is null) continue;

                var signature = arguments.Count == 0
                    ? identity
                    : identity + "<" + string.Join(", ", arguments) + ">";
                if (!resolutionBySignature.TryGetValue(signature, out var resolution)) continue;
                if (resolution.Implementation is null) continue;
                if (!contextsByFullName.TryGetValue(resolution.Implementation, out var context)) continue;

                edges.Add(new ResolvedEdge
                {
                    Kind = FlowEdgeKind.HandlerToDbContextResolved,
                    From = handler.FullName,
                    Via = resolution.InterfaceName,
                    To = context.FullName,
                    ProjectName = context.ProjectName,
                    Location = context.Location,
                });
            }
        }

        result.ResolvedEdges.AddRange(edges
            .GroupBy(edge => (edge.Kind, edge.From, edge.Via, edge.To))
            .Select(group => group.First())
            .OrderBy(edge => edge.From, StringComparer.Ordinal)
            .ThenBy(edge => edge.Via, StringComparer.Ordinal)
            .ThenBy(edge => edge.To, StringComparer.Ordinal));
    }

    private static IEnumerable<HandlerDependencies> EnumerateHandlers(AnalysisResult result)
    {
        foreach (var handler in result.Handlers)
            yield return new HandlerDependencies(handler.FullName, handler.Namespace, handler.Dependencies);
        foreach (var handler in result.NotificationHandlers)
            yield return new HandlerDependencies(handler.FullName, handler.Namespace, handler.Dependencies);
    }

    private sealed record HandlerDependencies(
        string FullName,
        string Namespace,
        IReadOnlyList<string> Dependencies);
}
