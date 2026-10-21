using System.Text.RegularExpressions;

namespace CqrsLens;

/// <summary>
/// Lightweight text-based detection of MediatR / FluentValidation / EF Core shapes.
/// Deliberately uses pattern matching over source text (no Roslyn, no build) — fast
/// and good enough for a map; it can miss exotic declarations (see README limitations).
///
/// Each file is scanned into a self-contained <see cref="FileScan"/> fragment so files
/// can be processed in parallel; fragments are merged by the analyzer.
/// </summary>
public static partial class CSharpScanner
{
    /// <summary>Everything found in one source file.</summary>
    public sealed class FileScan
    {
        public required string Namespace { get; init; }
        public List<ClassDecl> Classes { get; } = new();

        /// <summary>Interface name → its base interface names as written, generics stripped.</summary>
        public Dictionary<string, List<string>> Interfaces { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>A class/record/struct declaration and the shapes it implements.</summary>
    public sealed class ClassDecl
    {
        public required string Name { get; init; }
        public required string BaseList { get; init; }

        /// <summary>All base type names from the declaration, generics stripped.</summary>
        public required List<string> BaseTypeNames { get; init; }

        /// <summary>Simple names for the same base types.</summary>
        public required List<string> BaseRawNames { get; init; }

        /// <summary>Span of the class body in the source text (for DbSet attribution).</summary>
        public required (int Start, int End) BodySpan { get; init; }
    }

    // Matches: class|record|struct Name<...> (primary-ctor) : <base list> { or ;
    [GeneratedRegex(@"(?:class|record(?:\s+class|\s+struct)?|struct)\s+(\w+)(?:<[^>]*>)?\s*(?:\([^)]*\))?\s*:\s*([^{;]+?)(?:\s+where\s+\w+\s*:[^{;]+)?[{;]",
        RegexOptions.Singleline)]
    private static partial Regex DeclarationRegex();

    // Matches: interface Name<...> : <base list> { or ;
    [GeneratedRegex(@"\binterface\s+(\w+)(?:<[^>]*>)?\s*(?::\s*([^{;]+?))?(?:\s+where\s+\w+\s*:[^{;]+)?[{;]",
        RegexOptions.Singleline)]
    private static partial Regex InterfaceDeclarationRegex();

    // File-scoped namespace: "namespace Foo.Bar;"
    [GeneratedRegex(@"^\s*namespace\s+([\w.]+)\s*;", RegexOptions.Multiline)]
    private static partial Regex FileScopedNamespaceRegex();

    // Block namespace: "namespace Foo.Bar {"
    [GeneratedRegex(@"\bnamespace\s+([\w.]+)\s*\{")]
    private static partial Regex BlockNamespaceRegex();

    [GeneratedRegex(@"\bDbSet\s*<\s*(\w+)\s*>\s+(\w+)")]
    private static partial Regex DbSetRegex();

    [GeneratedRegex(@"(?:^|[,\s])(\w*DbContext)\b")]
    private static partial Regex DbContextBaseRegex();

    [GeneratedRegex(@"[a-zA-Z_][\w.]*")]
    private static partial Regex IdentifierRegex();

    /// <summary>Extracts the namespace declared in a source file ("" when none).</summary>
    public static string ExtractNamespace(string code)
    {
        var fs = FileScopedNamespaceRegex().Match(code);
        if (fs.Success) return fs.Groups[1].Value;
        var block = BlockNamespaceRegex().Match(code);
        return block.Success ? block.Groups[1].Value : "";
    }

    /// <summary>The final segment of a possibly dotted name.</summary>
    public static string SimpleName(string name)
    {
        var idx = name.LastIndexOf('.');
        return idx >= 0 ? name[(idx + 1)..] : name;
    }

    /// <summary>The namespace part of a dotted name ("" when the name is simple).</summary>
    public static string NamespaceOf(string name)
    {
        var idx = name.LastIndexOf('.');
        return idx >= 0 ? name[..idx] : "";
    }

    /// <summary>Base-type names from a base list, generic arguments stripped.</summary>
    public static List<string> ExtractBaseTypeNames(string baseList)
    {
        var names = new List<string>();
        foreach (var part in SplitTopLevel(baseList))
        {
            var angle = part.IndexOf('<');
            var raw = (angle >= 0 ? part[..angle] : part).Trim();
            if (raw.Length == 0) continue;
            var id = IdentifierRegex().Match(raw);
            if (id.Success)
                names.Add(id.Value);
        }
        return names;
    }

    public static List<string> ExtractBaseRawNames(string baseList)
        => ExtractBaseTypeNames(baseList).Select(SimpleName).ToList();

    /// <summary>Scans one file's text into a fragment (pure; safe to run in parallel).</summary>
    public static FileScan ScanText(string code)
    {
        var scan = new FileScan { Namespace = ExtractNamespace(code) };

        foreach (Match m in InterfaceDeclarationRegex().Matches(code))
        {
            var name = m.Groups[1].Value;
            if (!scan.Interfaces.ContainsKey(name))
                scan.Interfaces[name] = new List<string>();
            scan.Interfaces[name].AddRange(ExtractBaseTypeNames(m.Groups[2].Value));
        }

        foreach (Match m in DeclarationRegex().Matches(code))
        {
            var typeName = m.Groups[1].Value;
            var baseList = m.Groups[2].Value;
            // The match ends with the declaration's '{' (class with body) or ';'
            // (primary-constructor record/struct without a body).
            var openBrace = m.Value.EndsWith('{') ? m.Index + m.Length - 1 : -1;
            var span = ComputeClassSpan(code, m.Index, openBrace);
            scan.Classes.Add(new ClassDecl
            {
                Name = typeName,
                BaseList = baseList,
                BaseTypeNames = ExtractBaseTypeNames(baseList),
                BaseRawNames = ExtractBaseRawNames(baseList),
                BodySpan = span,
            });
        }

        return scan;
    }

    /// <summary>
    /// Finds the span covered by a class declaration: from its start to the matching
    /// close brace of its body. Falls back to just the declaration when no body exists.
    /// </summary>
    private static (int Start, int End) ComputeClassSpan(string code, int declStart, int openBrace)
    {
        if (openBrace < 0) return (declStart, declStart);
        var depth = 0;
        for (var i = openBrace; i < code.Length; i++)
        {
            if (code[i] == '{') depth++;
            else if (code[i] == '}')
            {
                depth--;
                if (depth == 0) return (declStart, i + 1);
            }
        }
        return (declStart, code.Length);
    }

    /// <summary>
    /// Turns one file's fragment into analysis entries. DbSets are attributed to the
    /// DbContext class whose body span contains them (supports several contexts per file).
    /// </summary>
    public static void ApplyScan(FileScan scan, string code, string? projectName, AnalysisResult result)
    {
        var contextClasses = new List<(ClassDecl Decl, DbContextInfo Info)>();

        foreach (var decl in scan.Classes)
        {
            // IRequest<...> / IRequest  -> a CQRS request
            if (Regex.IsMatch(decl.BaseList, @"\bIRequest\b"))
            {
                var response = ExtractGenericArguments(decl.BaseList, "IRequest")?.FirstOrDefault();
                result.Requests.Add(new CqrsRequest
                {
                    Name = decl.Name,
                    Namespace = scan.Namespace,
                    ResponseType = response,
                    ProjectName = projectName,
                });
            }

            // IRequestHandler<TRequest, TResponse> -> a handler
            var handlerArgs = ExtractGenericArguments(decl.BaseList, "IRequestHandler");
            if (handlerArgs is { Count: >= 1 })
            {
                result.Handlers.Add(new HandlerInfo
                {
                    Name = decl.Name,
                    Namespace = scan.Namespace,
                    RequestName = SimpleName(handlerArgs[0]),
                    RequestNamespace = NamespaceOf(handlerArgs[0]) is { Length: > 0 } ns ? ns : null,
                    ProjectName = projectName,
                });
            }

            // AbstractValidator<T> / IValidator<T> -> a validator for T
            var validatorTarget = ExtractGenericArguments(decl.BaseList, "AbstractValidator")?.FirstOrDefault()
                                  ?? ExtractGenericArguments(decl.BaseList, "IValidator")?.FirstOrDefault();
            if (validatorTarget is not null)
            {
                result.Validators.Add(new ValidatorInfo
                {
                    Name = decl.Name,
                    Namespace = scan.Namespace,
                    TargetType = SimpleName(validatorTarget),
                    TargetNamespace = NamespaceOf(validatorTarget) is { Length: > 0 } ns ? ns : null,
                    ProjectName = projectName,
                });
            }

            // IPipelineBehavior<...> -> a pipeline behavior
            if (decl.BaseList.Contains("IPipelineBehavior<", StringComparison.Ordinal))
            {
                result.Behaviors.Add(new BehaviorInfo { Name = decl.Name, ProjectName = projectName });
            }

            // : DbContext (or *DbContext, e.g. IdentityDbContext) -> a DbContext
            if (DbContextBaseRegex().IsMatch(decl.BaseList))
            {
                var ctx = new DbContextInfo
                {
                    Name = decl.Name,
                    Namespace = scan.Namespace,
                    ProjectName = projectName,
                };
                contextClasses.Add((decl, ctx));
                result.DbContexts.Add(ctx);
            }
        }

        if (contextClasses.Count > 0)
        {
            foreach (Match m in DbSetRegex().Matches(code))
            {
                var owner = contextClasses
                    .Where(c => m.Index >= c.Decl.BodySpan.Start && m.Index < c.Decl.BodySpan.End)
                    .OrderByDescending(c => c.Decl.BodySpan.Start)
                    .FirstOrDefault();
                if (owner.Info is null) continue;
                var set = new DbSetInfo(m.Groups[1].Value, m.Groups[2].Value);
                if (!owner.Info.DbSets.Contains(set))
                    owner.Info.DbSets.Add(set);
            }
        }
    }

    /// <summary>
    /// Convenience for tests and single-file scans: scan text directly into a result.
    /// (v0.1-compatible entry point; files scanned this way get no marker resolution.)
    /// </summary>
    public static void ScanText(string code, string? projectName, AnalysisResult result)
        => ApplyScan(ScanText(code), code, projectName, result);

    public static void ScanFile(string path, string? projectName, AnalysisResult result)
        => ScanText(File.ReadAllText(path), projectName, result);

    /// <summary>
    /// Extracts the top-level generic arguments of <paramref name="interfaceName"/>
    /// as written in a base list, e.g. IRequestHandler&lt;Foo, Bar&gt; -> ["Foo", "Bar"].
    /// Returns null when the interface (with generic arguments) is not present.
    /// </summary>
    public static List<string>? ExtractGenericArguments(string baseList, string interfaceName)
    {
        var idx = baseList.IndexOf(interfaceName, StringComparison.Ordinal);
        while (idx >= 0)
        {
            var after = idx + interfaceName.Length;
            // The next char must be '<' (possibly after spaces) — otherwise this is a
            // longer identifier that merely starts with the same text.
            var i = after;
            while (i < baseList.Length && char.IsWhiteSpace(baseList[i])) i++;
            if (i < baseList.Length && baseList[i] == '<')
            {
                var depth = 0;
                var start = i + 1;
                for (var j = i; j < baseList.Length; j++)
                {
                    if (baseList[j] == '<') depth++;
                    else if (baseList[j] == '>')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            var inner = baseList.Substring(start, j - start);
                            return SplitTopLevel(inner);
                        }
                    }
                }
                return null;
            }
            idx = baseList.IndexOf(interfaceName, idx + 1, StringComparison.Ordinal);
        }
        return null;
    }

    private static List<string> SplitTopLevel(string inner)
    {
        var parts = new List<string>();
        var depth = 0;
        var current = "";
        foreach (var c in inner)
        {
            if (c == '<') depth++;
            if (c == '>') depth--;
            if (c == ',' && depth == 0)
            {
                parts.Add(current.Trim());
                current = "";
            }
            else current += c;
        }
        if (current.Trim().Length > 0) parts.Add(current.Trim());
        return parts;
    }
}
