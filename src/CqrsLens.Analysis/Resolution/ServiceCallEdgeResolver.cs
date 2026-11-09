using System.Text.RegularExpressions;

namespace CqrsLens;

/// <summary>
/// Stage 2 of the service-resolution work: method-body call edges between scanned classes.
///
/// A <em>source</em> is a request/notification handler, or a service — a concrete class that is
/// the chosen implementation of at least one interface in the Stage 1 index and is injected as
/// a constructor dependency into at least one scanned class. An <em>injected member</em> is a
/// field, property or constructor parameter whose declared type comes from the source's
/// constructor signature. A <em>call edge</em> is an invocation whose target expression is
/// exactly such a member; the target resolves through the Stage 1 index when the member is an
/// interface, or is the concrete class itself when the member is a scanned class.
///
/// Fail closed: property chains, locals, parameters, <c>new</c>-constructed targets, property
/// access, statics, <c>base</c>/<c>this</c>, DbContext targets, self-calls and
/// unresolved/ambiguous interfaces produce no edge.
/// </summary>
internal static partial class ServiceCallEdgeResolver
{
    // A member-access invocation: an identifier receiver (not preceded by `.`, so a property
    // chain's tail like `UnitOfWork.SaveEntitiesAsync` is rejected), then the member access and
    // the invoked method name. The target expression is the receiver; the method name is
    // captured without arguments or signature.
    [GeneratedRegex(
        @"(?<![\w.@])(?<receiver>@?[A-Za-z_]\w*)\s*[!?]?\s*\.\s*(?<method>[A-Za-z_]\w*)\s*(?:<[^;{}()]*>)?\s*\(")]
    private static partial Regex InvocationRegex();

    public static void Build(
        IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles,
        AnalysisResult result)
    {
        if (parsedFiles.Count == 0) return;

        var ordered = parsedFiles
            .OrderBy(f => f.File.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var classes = new List<CSharpScanner.ClassDecl>();
        var interfaces = new List<CSharpScanner.InterfaceDecl>();
        var classByFullName = new Dictionary<string, CSharpScanner.ClassDecl>(StringComparer.Ordinal);
        foreach (var (_, scan, _) in ordered)
        {
            foreach (var declaration in scan.Classes)
            {
                classes.Add(declaration);
                classByFullName.TryAdd(declaration.FullName, declaration);
            }
            interfaces.AddRange(scan.InterfaceDecls);
        }
        if (classes.Count == 0) return;

        var hierarchy = new TypeHierarchy(classes, interfaces);

        var resolutionBySignature = new Dictionary<string, ServiceResolution>(StringComparer.Ordinal);
        foreach (var resolution in result.ServiceResolutions)
            resolutionBySignature.TryAdd(resolution.Interface, resolution);

        var contexts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var context in result.DbContexts)
            contexts.Add(context.FullName);

        // Source classes: the handler inventory plus services (chosen implementations injected
        // into at least one scanned class).
        var sourceFullNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var handler in result.Handlers)
            sourceFullNames.Add(handler.FullName);
        foreach (var handler in result.NotificationHandlers)
            sourceFullNames.Add(handler.FullName);

        AddServices(ordered, hierarchy, resolutionBySignature, classByFullName, sourceFullNames);

        // A handler that is not a scanned class produces no body to scan.
        sourceFullNames.RemoveWhere(name => !classByFullName.ContainsKey(name));
        if (sourceFullNames.Count == 0) return;

        var edges = new Dictionary<(string From, string Via, string Method, string To), ServiceCallEdge>();
        foreach (var parsed in ordered)
        {
            // Only classes that are sources need their bodies scanned.
            foreach (var declaration in parsed.Scan.Classes)
            {
                if (!sourceFullNames.Contains(declaration.FullName)) continue;
                ScanClass(parsed, declaration, classes, hierarchy, resolutionBySignature, contexts, result, edges);
            }
        }

        result.ServiceCallEdges.AddRange(edges.Values
            .OrderBy(edge => edge.From, StringComparer.Ordinal)
            .ThenBy(edge => edge.Via, StringComparer.Ordinal)
            .ThenBy(edge => edge.Method, StringComparer.Ordinal)
            .ThenBy(edge => edge.To, StringComparer.Ordinal));
    }

    /// <summary>
    /// A service is a concrete class that is the chosen implementation of an interface in the
    /// index and is injected as a constructor dependency into at least one scanned class.
    /// </summary>
    private static void AddServices(
        IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles,
        TypeHierarchy hierarchy,
        IReadOnlyDictionary<string, ServiceResolution> resolutionBySignature,
        IReadOnlyDictionary<string, CSharpScanner.ClassDecl> classByFullName,
        HashSet<string> sourceFullNames)
    {
        if (resolutionBySignature.Count == 0) return;

        var injected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (file, scan, mask) in parsedFiles)
        {
            foreach (var declaration in scan.Classes)
            {
                var dependencies = new List<string>();
                DependencyExtractor.Extract(declaration, file.Code, mask, scan.Classes, dependencies);
                foreach (var dependency in dependencies)
                {
                    var signature = SignatureFor(hierarchy, dependency, declaration, resolveFallback: true);
                    if (signature is not null) injected.Add(signature);
                }
            }
        }

        if (injected.Count == 0) return;

        foreach (var resolution in resolutionBySignature.Values)
        {
            if (resolution.Implementation is null) continue;
            if (!injected.Contains(resolution.Interface)) continue;
            if (!classByFullName.ContainsKey(resolution.Implementation)) continue;
            sourceFullNames.Add(resolution.Implementation);
        }
    }

    private static void ScanClass(
        (SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask) parsed,
        CSharpScanner.ClassDecl declaration,
        IReadOnlyList<CSharpScanner.ClassDecl> classes,
        TypeHierarchy hierarchy,
        IReadOnlyDictionary<string, ServiceResolution> resolutionBySignature,
        IReadOnlySet<string> contexts,
        AnalysisResult result,
        Dictionary<(string From, string Via, string Method, string To), ServiceCallEdge> edges)
    {
        var members = new List<(string Type, string Name)>();
        DependencyExtractor.ExtractParameters(declaration, parsed.File.Code, parsed.Mask, parsed.Scan.Classes, members);
        if (members.Count == 0) return;

        var constructorTypes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (type, _) in members)
            constructorTypes.Add(Normalize(type));

        ReceiverTypeMapBuilder.CollectClassLevelDeclarations(
            declaration, parsed.File.Code, parsed.Mask, parsed.Scan.Classes, members);
        foreach (var property in declaration.Properties)
        {
            if (property.Name.Length == 0) continue;
            if (!members.Contains((Normalize(property.Type), property.Name)))
                members.Add((Normalize(property.Type), property.Name));
        }

        // Keep only members whose declared type comes from the constructor signature.
        var typeByMember = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (type, name) in members)
        {
            if (name.Length == 0 || type.Length == 0) continue;
            if (!constructorTypes.Contains(type)) continue;
            typeByMember.TryAdd(name, type);
        }
        if (typeByMember.Count == 0) return;

        var code = parsed.File.Code;
        var bodyEnd = Math.Min(code.Length, declaration.BodySpan.End);
        foreach (Match match in InvocationRegex().Matches(code, declaration.BodySpan.Start))
        {
            if (match.Index >= bodyEnd) break;
            if (!parsed.Mask.IsCode(match.Index)) continue;
            if (!ReferenceEquals(DeepSliceSyntax.InnermostClass(parsed.Scan.Classes, match.Index), declaration))
                continue;

            var receiver = match.Groups["receiver"].Value.TrimStart('@');
            if (!typeByMember.TryGetValue(receiver, out var memberType)) continue;

            var method = match.Groups["method"].Value;
            if (method.Length == 0) continue;

            if (!TryResolveTarget(memberType, declaration, hierarchy, resolutionBySignature, contexts,
                    out var via, out var to))
                continue;
            if (to == declaration.FullName) continue;

            var key = (declaration.FullName, via, method, to);
            if (edges.ContainsKey(key)) continue;

            edges[key] = new ServiceCallEdge
            {
                From = declaration.FullName,
                Via = via,
                Method = method,
                To = to,
                ProjectName = parsed.File.ProjectName,
                Location = SourceLocation.Create(parsed.File.Path, result.RootPath, code, match.Index),
            };
        }
    }

    private static bool TryResolveTarget(
        string memberType,
        CSharpScanner.ClassDecl declaring,
        TypeHierarchy hierarchy,
        IReadOnlyDictionary<string, ServiceResolution> resolutionBySignature,
        IReadOnlySet<string> contexts,
        out string via,
        out string to)
    {
        via = "";
        to = "";

        var (name, arguments) = CSharpScanner.ParseTypeReference(memberType);
        if (name.Length == 0) return false;

        var identity = hierarchy.ResolveInterfaceReference(
            name, declaring.Namespace, declaring.Usings, declaring.ContainingTypes, arguments.Count, out _);
        if (identity is null)
        {
            // An interface declared outside the scan keeps its simple name, exactly as the
            // Stage 1 index records a registration for an external interface.
            identity = CSharpScanner.SimpleName(name);
            if (arguments.Count > 0 && !identity.EndsWith("`" + arguments.Count, StringComparison.Ordinal))
                identity = CSharpScanner.TypeIdentity(identity, arguments.Count);
        }

        var signature = arguments.Count == 0
            ? identity
            : identity + "<" + string.Join(", ", arguments) + ">";

        if (resolutionBySignature.TryGetValue(signature, out var resolution))
        {
            // Resolved through the index: ambiguous or unresolved yields no edge, and a
            // DbContext implementation is already represented by the Stage 1 dependency edges.
            if (resolution.Implementation is null) return false;
            if (contexts.Contains(resolution.Implementation)) return false;
            via = resolution.InterfaceName;
            to = resolution.Implementation;
            return true;
        }

        // Not an index interface: a concrete class declared in the scan is a direct edge.
        var classDecl = hierarchy.TryResolveClass(
            name, declaring.Namespace, declaring.Usings, declaring.ContainingTypes, arguments.Count, out _);
        if (classDecl is null) return false;
        if (contexts.Contains(classDecl.FullName)) return false;
        via = classDecl.FullName;
        to = classDecl.FullName;
        return true;
    }

    /// <summary>
    /// The interface signature a written type resolves to, mirroring the Stage 1 index keying:
    /// resolved identity when the interface is in the scan, otherwise the simple name (with an
    /// arity suffix when generic).
    /// </summary>
    private static string? SignatureFor(
        TypeHierarchy hierarchy,
        string writtenType,
        CSharpScanner.ClassDecl declaring,
        bool resolveFallback)
    {
        var (name, arguments) = CSharpScanner.ParseTypeReference(writtenType);
        if (name.Length == 0) return null;

        var identity = hierarchy.ResolveInterfaceReference(
            name, declaring.Namespace, declaring.Usings, declaring.ContainingTypes, arguments.Count, out _);
        if (identity is null)
        {
            if (!resolveFallback) return null;
            identity = CSharpScanner.SimpleName(name);
            if (arguments.Count > 0 && !identity.EndsWith("`" + arguments.Count, StringComparison.Ordinal))
                identity = CSharpScanner.TypeIdentity(identity, arguments.Count);
        }

        return arguments.Count == 0
            ? identity
            : identity + "<" + string.Join(", ", arguments) + ">";
    }

    private static string Normalize(string type) => Regex.Replace(type, @"\s+", "");
}
