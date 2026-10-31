using System.Text.RegularExpressions;

namespace CqrsLens;

/// <summary>
/// Discovers Wolverine convention handlers: classes named <c>*Handler</c> / <c>*Consumer</c>
/// with <c>Handle</c>/<c>Consume</c> methods whose first parameter is the message type.
/// </summary>
internal static partial class WolverineHandlerDiscovery
{
    /// <summary>
    /// Adds Wolverine convention handlers (and the messages they handle) to the
    /// result. Runs after the backend scan and before linking. Messages are only
    /// added when their type is declared in the scanned source, so framework
    /// types and primitives never become requests.
    /// </summary>
    public static void Apply(IReadOnlyList<SourceFile> files, AnalysisResult result)
    {
        var scans = new List<(SourceFile File, CSharpScanner.FileScan Scan)>();
        foreach (var file in files)
        {
            try
            {
                scans.Add((file, CSharpScanner.ScanText(file.Code)));
            }
            catch
            {
                // Best effort: the backend scanner already reports unreadable files.
            }
        }

        var declarations = new Dictionary<string, List<(CSharpScanner.ClassDecl Decl, SourceFile File)>>(StringComparer.Ordinal);
        foreach (var (file, scan) in scans)
        {
            foreach (var decl in scan.Classes)
            {
                if (!declarations.TryGetValue(decl.Name, out var list))
                    declarations[decl.Name] = list = new();
                list.Add((decl, file));
            }
        }

        var knownHandlers = new HashSet<string>(result.Handlers.Select(h => h.FullName), StringComparer.Ordinal);
        var addedPairs = new HashSet<(string Handler, string Message)>();
        var requestsByFullName = result.Requests
            .GroupBy(r => r.FullName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var (file, scan) in scans)
        {
            foreach (var decl in scan.Classes)
            {
                if (!IsHandlerName(decl.Name) || decl.IsAbstract) continue;
                if (knownHandlers.Contains(decl.FullName)) continue;

                var body = BodyText(file.Code, decl, scan.Classes);
                foreach (Match match in WolverineMethodRegex().Matches(body.Text))
                {
                    var rawType = match.Groups["type"].Value.TrimEnd('?');
                    var simple = CSharpScanner.SimpleName(CSharpScanner.UnqualifiedTypeName(rawType));
                    var writtenNs = CSharpScanner.NamespaceOfType(rawType);
                    var message = ResolveMessage(simple, writtenNs, decl, declarations);
                    if (message is null) continue;

                    var (msgDecl, msgFile) = message.Value;
                    if (IsHandlerName(msgDecl.Name)) continue;
                    // Open-generic messages are wrapper shapes, not concrete requests.
                    if (msgDecl.TypeParameters.Count > 0)
                    {
                        result.NonConcreteRequestNames.Add(msgDecl.Name);
                        continue;
                    }
                    if (!addedPairs.Add((decl.FullName, msgDecl.FullName))) continue;

                    if (!requestsByFullName.TryGetValue(msgDecl.FullName, out var request))
                    {
                        request = new CqrsRequest
                        {
                            Name = msgDecl.Name,
                            Namespace = msgDecl.Namespace,
                            ProjectName = msgFile.ProjectName,
                            ViaMarker = MediatorConventions.WolverineName,
                            Location = SourceLocation.Create(
                                msgFile.Path, result.RootPath, msgFile.Code, msgDecl.DeclarationIndex),
                        };
                        request.ContainingTypes.AddRange(msgDecl.ContainingTypes);
                        result.Requests.Add(request);
                        requestsByFullName[request.FullName] = request;
                    }

                    var handler = new HandlerInfo
                    {
                        Name = decl.Name,
                        Namespace = decl.Namespace,
                        RequestName = msgDecl.Name,
                        RequestNamespace = msgDecl.ContainingTypes.Count == 0
                            ? msgDecl.Namespace
                            : string.Join(".", new[] { msgDecl.Namespace }
                                .Where(n => !string.IsNullOrEmpty(n))
                                .Concat(msgDecl.ContainingTypes)),
                        ProjectName = file.ProjectName,
                        Location = SourceLocation.Create(
                            file.Path, result.RootPath, file.Code, body.Start + match.Index),
                    };
                    handler.ContainingTypes.AddRange(decl.ContainingTypes);
                    result.Handlers.Add(handler);
                }
            }
        }
    }

    [GeneratedRegex(
        @"\b(?<method>Handle|HandleAsync|Handles|HandlesAsync|Consume|ConsumeAsync)\s*\(\s*(?:\[[^\]]*\]\s*)?(?:(?:this|in|ref|scoped)\s+)?(?<type>[A-Za-z_][\w.]*(?:<[^()]*?>)?\??)\s+[A-Za-z_]\w*\s*[,)=]")]
    private static partial Regex WolverineMethodRegex();

    internal static bool IsHandlerName(string name)
        => name.Length > "Handler".Length &&
           (name.EndsWith("Handler", StringComparison.Ordinal) ||
            name.EndsWith("Consumer", StringComparison.Ordinal) ||
            name.EndsWith("Handlers", StringComparison.Ordinal) ||
            name.EndsWith("Consumers", StringComparison.Ordinal));

    private static (int Start, string Text) BodyText(
        string code,
        CSharpScanner.ClassDecl decl,
        IReadOnlyList<CSharpScanner.ClassDecl> all)
    {
        var (start, end) = decl.BodySpan;
        start = Math.Clamp(start, 0, code.Length);
        end = Math.Clamp(end, start, code.Length);
        var chars = code.Substring(start, end - start).ToCharArray();

        // Blank out nested type bodies so their methods are attributed to them.
        foreach (var nested in all)
        {
            if (ReferenceEquals(nested, decl)) continue;
            var (ns, ne) = nested.BodySpan;
            var declStart = Math.Max(nested.DeclarationIndex, 0);
            if (declStart <= start || ne > end) continue;
            for (var i = declStart; i < ne && i < end; i++)
                chars[i - start] = ' ';
        }

        return (start, new string(chars));
    }

    private static (CSharpScanner.ClassDecl Decl, SourceFile File)? ResolveMessage(
        string simple,
        string writtenNs,
        CSharpScanner.ClassDecl handler,
        Dictionary<string, List<(CSharpScanner.ClassDecl Decl, SourceFile File)>> declarations)
    {
        if (!declarations.TryGetValue(simple, out var candidates) || candidates.Count == 0)
            return null;
        if (candidates.Count == 1)
            return candidates[0];

        bool Matches(CSharpScanner.ClassDecl d, string ns)
            => string.Equals(d.Namespace, ns, StringComparison.Ordinal) ||
               string.Equals(
                   string.Join(".", new[] { d.Namespace }.Where(n => n.Length > 0).Concat(d.ContainingTypes)),
                   ns,
                   StringComparison.Ordinal);

        if (writtenNs.Length > 0)
        {
            var qualified = candidates.Where(c => Matches(c.Decl, writtenNs) || c.Decl.Namespace.EndsWith("." + writtenNs, StringComparison.Ordinal)).ToList();
            if (qualified.Count == 1) return qualified[0];
        }

        var sameNs = candidates.Where(c => Matches(c.Decl, handler.Namespace)).ToList();
        if (sameNs.Count == 1) return sameNs[0];

        var imported = candidates
            .Where(c => handler.Usings.Any(u => string.Equals(u.Target, c.Decl.Namespace, StringComparison.Ordinal)))
            .ToList();
        return imported.Count == 1 ? imported[0] : null;
    }
}
