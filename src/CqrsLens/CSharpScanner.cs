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
        public List<InterfaceDecl> InterfaceDecls { get; } = new();

        /// <summary>Interface name → its base interface names as written, generics stripped.</summary>
        public Dictionary<string, List<string>> Interfaces { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>A class/record/struct declaration and the shapes it implements.</summary>
    public sealed class ClassDecl
    {
        public required string Name { get; init; }
        public required string Namespace { get; init; }
        public required string BaseList { get; init; }

        /// <summary>All base type names from the declaration, generics stripped.</summary>
        public required List<string> BaseTypeNames { get; init; }

        /// <summary>Simple names for the same base types.</summary>
        public required List<string> BaseRawNames { get; init; }

        /// <summary>Declared generic parameter names (e.g. TRequest, TResponse).</summary>
        public List<string> TypeParameters { get; init; } = new();

        public bool IsAbstract { get; init; }

        /// <summary>Span of the class body in the source text (for DbSet attribution).</summary>
        public required (int Start, int End) BodySpan { get; init; }

        public string FullName => string.IsNullOrEmpty(Namespace) ? Name : $"{Namespace}.{Name}";
    }

    /// <summary>An interface declaration, including its raw base list for generic substitution.</summary>
    public sealed class InterfaceDecl
    {
        public required string Name { get; init; }
        public required string Namespace { get; init; }
        public required string BaseList { get; init; }
        public List<string> TypeParameters { get; init; } = new();
        public string FullName => string.IsNullOrEmpty(Namespace) ? Name : $"{Namespace}.{Name}";
    }

    // Matches: class|record|struct Name<...> (primary-ctor) : <base list> { or ;
    [GeneratedRegex(@"(?:class|record(?:\s+class|\s+struct)?|struct)\s+(\w+)(?:<([^>]*)>)?\s*(?:\([^{};]*\))?\s*:\s*([^{;]+?)(?:\s+where\s+\w+\s*:[^{;]+)?[{;]",
        RegexOptions.Singleline)]
    private static partial Regex DeclarationRegex();

    // Matches: interface Name<...> : <base list> { or ;
    [GeneratedRegex(@"\binterface\s+(\w+)(?:<([^>]*)>)?\s*(?::\s*([^{;]+?))?(?:\s+where\s+\w+\s*:[^{;]+)?[{;]",
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

    /// <summary>
    /// The simple type name, removing namespace, generic arguments and nullable
    /// annotations: <c>App.Queries.GetThing&lt;int&gt;?</c> → <c>GetThing</c>.
    /// </summary>
    public static string UnqualifiedTypeName(string name)
    {
        var value = StripGenericArguments(name.Trim());
        value = value.TrimEnd('?');
        value = value.TrimEnd('[', ']');
        var idx = value.LastIndexOf('.');
        return idx >= 0 ? value[(idx + 1)..] : value;
    }

    /// <summary>The namespace part of a type reference, or "" when it is unqualified.</summary>
    public static string NamespaceOfType(string name)
    {
        var value = StripGenericArguments(name.Trim());
        value = value.TrimEnd('?');
        value = value.TrimEnd('[', ']');
        var idx = value.LastIndexOf('.');
        return idx >= 0 ? value[..idx] : "";
    }

    private static string StripGenericArguments(string value)
    {
        if (value.StartsWith("global::", StringComparison.Ordinal))
            value = value[8..];
        var angle = value.IndexOf('<');
        return angle >= 0 ? value[..angle].Trim() : value;
    }

    /// <summary>The namespace part of a dotted name ("" when the name is simple).</summary>
    public static string NamespaceOf(string name)
    {
        var idx = name.LastIndexOf('.');
        return idx >= 0 ? name[..idx] : "";
    }

    /// <summary>Parses declared generic parameters, discarding variance markers.</summary>
    public static List<string> ParseTypeParameters(string? parameters)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(parameters)) return result;
        foreach (var raw in SplitTopLevel(parameters))
        {
            var name = raw.Trim();
            if (name.StartsWith("in ", StringComparison.Ordinal) ||
                name.StartsWith("out ", StringComparison.Ordinal))
                name = name[3..].Trim();
            if (name.Length > 0) result.Add(name);
        }
        return result;
    }

    /// <summary>Base-type names from a base list, generic arguments stripped.</summary>
    public static List<string> ExtractBaseTypeNames(string baseList)
    {
        var names = new List<string>();
        foreach (var part in SplitTopLevel(baseList))
        {
            var (raw, _) = ParseTypeReference(part);
            if (raw.Length > 0)
                names.Add(raw);
        }
        return names;
    }

    public static List<string> ExtractBaseRawNames(string baseList)
        => ExtractBaseTypeNames(baseList).Select(SimpleName).ToList();

    /// <summary>
    /// Splits a type reference into its name and top-level generic arguments:
    /// <c>App.IHandler&lt;Foo, Bar&lt;int&gt;&gt;</c> → name + ["Foo", "Bar&lt;int&gt;"].
    /// </summary>
    public static (string Name, List<string> Arguments) ParseTypeReference(string typeReference)
    {
        var text = typeReference.Trim();
        if (text.StartsWith("global::", StringComparison.Ordinal))
            text = text[8..];

        var angle = text.IndexOf('<');
        if (angle < 0)
            return (text.Trim(), new List<string>());

        var name = text[..angle].Trim();
        var depth = 0;
        var start = angle + 1;
        for (var i = angle; i < text.Length; i++)
        {
            if (text[i] == '<') depth++;
            else if (text[i] == '>')
            {
                depth--;
                if (depth == 0)
                    return (name, SplitTopLevel(text.Substring(start, i - start)));
            }
        }
        return (name, new List<string>());
    }

    /// <summary>
    /// True when the declaration immediately preceding <paramref name="declarationIndex"/>
    /// carries the <c>abstract</c> modifier. Best-effort for line-wrapped modifiers.
    /// </summary>
    public static bool IsAbstractDeclaration(string code, int declarationIndex)
    {
        var lineStart = code.LastIndexOf('\n', declarationIndex);
        var prefix = code[(lineStart + 1)..declarationIndex];
        if (prefix.Contains("abstract", StringComparison.Ordinal)) return true;

        var start = Math.Max(0, declarationIndex - 300);
        var segment = code[start..declarationIndex];
        var boundary = segment.LastIndexOfAny(new[] { ';', '}' });
        if (boundary >= 0) segment = segment[(boundary + 1)..];
        return System.Text.RegularExpressions.Regex.IsMatch(segment, @"\babstract\b");
    }

    /// <summary>Scans one file's text into a fragment (pure; safe to run in parallel).</summary>
    public static FileScan ScanText(string code)
    {
        var scan = new FileScan { Namespace = ExtractNamespace(code) };

        foreach (Match m in InterfaceDeclarationRegex().Matches(code))
        {
            var name = m.Groups[1].Value;
            var baseList = m.Groups[3].Value;
            scan.InterfaceDecls.Add(new InterfaceDecl
            {
                Name = name,
                Namespace = scan.Namespace,
                BaseList = baseList,
                TypeParameters = ParseTypeParameters(m.Groups[2].Value),
            });
            if (!scan.Interfaces.ContainsKey(name))
                scan.Interfaces[name] = new List<string>();
            scan.Interfaces[name].AddRange(ExtractBaseTypeNames(baseList));
        }

        foreach (Match m in DeclarationRegex().Matches(code))
        {
            var typeName = m.Groups[1].Value;
            var baseList = m.Groups[3].Value;
            // The match ends with the declaration's '{' (class with body) or ';'
            // (primary-constructor record/struct without a body).
            var openBrace = m.Value.EndsWith('{') ? m.Index + m.Length - 1 : -1;
            var span = ComputeClassSpan(code, m.Index, openBrace);
            scan.Classes.Add(new ClassDecl
            {
                Name = typeName,
                Namespace = scan.Namespace,
                BaseList = baseList,
                BaseTypeNames = ExtractBaseTypeNames(baseList),
                BaseRawNames = ExtractBaseRawNames(baseList),
                TypeParameters = ParseTypeParameters(m.Groups[2].Value),
                IsAbstract = IsAbstractDeclaration(code, m.Index),
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
            // Open generic / abstract base declarations are not concrete shapes.
            var typeParameters = new HashSet<string>(decl.TypeParameters, StringComparer.Ordinal);
            bool IsConcreteTarget(string? target) =>
                !decl.IsAbstract && target is not null && !typeParameters.Contains(target);

            // IRequest<...> / IRequest  -> a CQRS request
            if (!decl.IsAbstract && decl.BaseRawNames.Contains("IRequest"))
            {
                var response = ExtractGenericArguments(decl.BaseList, "IRequest")?.FirstOrDefault();
                result.Requests.Add(new CqrsRequest
                {
                    Name = decl.Name,
                    Namespace = decl.Namespace,
                    ResponseType = response,
                    ProjectName = projectName,
                });
            }

            // IRequestHandler<TRequest, TResponse> -> a handler for every interface.
            foreach (var handlerArgs in ExtractAllGenericArguments(decl.BaseList, "IRequestHandler"))
            {
                if (handlerArgs.Count < 1) continue;
                var requestType = handlerArgs[0];
                if (!IsConcreteTarget(UnqualifiedTypeName(requestType))) continue;
                if (result.Handlers.Any(h =>
                        h.Namespace == decl.Namespace &&
                        h.Name == decl.Name &&
                        h.RequestName == UnqualifiedTypeName(requestType)))
                    continue;

                result.Handlers.Add(new HandlerInfo
                {
                    Name = decl.Name,
                    Namespace = decl.Namespace,
                    RequestName = UnqualifiedTypeName(requestType),
                    RequestNamespace = NamespaceOfType(requestType) is { Length: > 0 } ns ? ns : null,
                    ProjectName = projectName,
                });
            }

            // AbstractValidator<T> / IValidator<T> -> a validator for every target.
            var validatorTargets = new List<string>();
            foreach (var root in new[] { "AbstractValidator", "IValidator" })
                validatorTargets.AddRange(ExtractAllGenericArguments(decl.BaseList, root)
                    .Where(a => a.Count >= 1)
                    .Select(a => a[0]));
            foreach (var validatorTarget in validatorTargets.Distinct(StringComparer.Ordinal))
            {
                if (!IsConcreteTarget(UnqualifiedTypeName(validatorTarget))) continue;
                if (result.Validators.Any(v =>
                        v.Namespace == decl.Namespace &&
                        v.Name == decl.Name &&
                        v.TargetType == UnqualifiedTypeName(validatorTarget)))
                    continue;

                result.Validators.Add(new ValidatorInfo
                {
                    Name = decl.Name,
                    Namespace = decl.Namespace,
                    TargetType = UnqualifiedTypeName(validatorTarget),
                    TargetNamespace = NamespaceOfType(validatorTarget) is { Length: > 0 } ns ? ns : null,
                    ProjectName = projectName,
                });
            }

            // IPipelineBehavior<...> -> a pipeline behavior
            if (decl.BaseList.Contains("IPipelineBehavior<", StringComparison.Ordinal))
            {
                result.Behaviors.Add(new BehaviorInfo { Name = decl.Name, ProjectName = projectName });
            }

            // : DbContext (or *DbContext, e.g. IdentityDbContext) -> a DbContext
            if (!decl.IsAbstract && DbContextBaseRegex().IsMatch(decl.BaseList))
            {
                var ctx = new DbContextInfo
                {
                    Name = decl.Name,
                    Namespace = decl.Namespace,
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
    /// Extracts the top-level generic arguments of the first
    /// <paramref name="interfaceName"/> occurrence in a base list, e.g.
    /// IRequestHandler&lt;Foo, Bar&gt; -> ["Foo", "Bar"]. Returns null when absent.
    /// </summary>
    public static List<string>? ExtractGenericArguments(string baseList, string interfaceName)
        => ExtractAllGenericArguments(baseList, interfaceName).FirstOrDefault();

    /// <summary>
    /// Extracts the generic arguments of every occurrence of <paramref name="interfaceName"/>
    /// in a base list, preserving declaration order.
    /// </summary>
    public static List<List<string>> ExtractAllGenericArguments(string baseList, string interfaceName)
    {
        var results = new List<List<string>>();
        foreach (var part in SplitTopLevel(baseList))
        {
            var (name, args) = ParseTypeReference(part);
            if (args.Count == 0) continue;
            if (string.Equals(SimpleName(name), interfaceName, StringComparison.Ordinal))
                results.Add(args);
        }
        return results;
    }

    public static List<string> SplitTopLevel(string inner)
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
