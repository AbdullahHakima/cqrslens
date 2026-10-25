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

        /// <summary>
        /// Local and global using directives in this file. Project-level global
        /// usings are appended by the text scanner before resolution runs.
        /// </summary>
        public List<UsingDirective> Usings { get; } = new();

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

        /// <summary>Using directives from the declaring file, shared with the scan.</summary>
        public List<UsingDirective> Usings { get; init; } = new();

        public bool IsAbstract { get; init; }

        public List<string> ContainingTypes { get; } = new();

        /// <summary>Span of the class body in the source text (for DbSet attribution).</summary>
        public required (int Start, int End) BodySpan { get; init; }

        /// <summary>Index of the declaration keyword in the source file.</summary>
        public int DeclarationIndex { get; init; }

        /// <summary>Primary-constructor parameter list, when the declaration has one.</summary>
        public string? PrimaryConstructorParameters { get; init; }

        /// <summary>Properties used by the EF Core ER model; parsed best-effort from source.</summary>
        public List<PropertyDecl> Properties { get; } = new();

        public string FullName
        {
            get
            {
                var typePath = ContainingTypes.Count == 0 ? Name : string.Join(".", ContainingTypes) + "." + Name;
                return string.IsNullOrEmpty(Namespace) ? typePath : $"{Namespace}.{typePath}";
            }
        }
    }

    /// <summary>
    /// A property or record primary-constructor parameter. Text mode uses this for
    /// EF Core entity discovery; it intentionally captures only shapes that map
    /// safely without a semantic model.
    /// </summary>
    public sealed class PropertyDecl
    {
        public required string Name { get; init; }
        public required string Type { get; init; }
        public bool IsNullable { get; init; }
        public bool HasSetter { get; init; }
        public bool IsPublic { get; init; }
        public bool IsStatic { get; init; }
        public bool IsPrimaryConstructorParameter { get; init; }
        public int SourceIndex { get; init; }

        /// <summary>Raw attribute text, including the surrounding brackets.</summary>
        public List<string> Attributes { get; } = new();
    }

    /// <summary>An interface declaration, including its raw base list for generic substitution.</summary>
    public sealed class InterfaceDecl
    {
        public required string Name { get; init; }
        public required string Namespace { get; init; }
        public required string BaseList { get; init; }
        public List<string> TypeParameters { get; init; } = new();

        /// <summary>Using directives from the declaring file, shared with the scan.</summary>
        public List<UsingDirective> Usings { get; init; } = new();

        public List<string> ContainingTypes { get; } = new();
        public required (int Start, int End) BodySpan { get; init; }

        public string FullName
        {
            get
            {
                var typePath = ContainingTypes.Count == 0 ? Name : string.Join(".", ContainingTypes) + "." + Name;
                return string.IsNullOrEmpty(Namespace) ? typePath : $"{Namespace}.{typePath}";
            }
        }
    }

    // Matches: class|record|struct Name<...> (primary-ctor) [: base list] { or ;
    // The primary-constructor parameter list may not run past a ") :" sequence:
    // that colon opens the base list (DbContext(options) is the first base entry
    // of a primary-constructor class, not part of its parameter list).
    [GeneratedRegex(@"(?:class|record(?:\s+class|\s+struct)?|struct)\s+(?<name>\w+)(?:<(?<typeparams>[^>]*)>)?\s*(?:\((?<params>(?:(?!\)\s*:)[^{};])*)\))?\s*(?:(?!where\b)\s*:\s*(?<base>[^{;]+?))?\s*(?:\s*where\s+\w+\s*:[^{;]+)?[{;]",
        RegexOptions.Singleline)]
    private static partial Regex DeclarationRegex();

    // Best-effort property match. The accessor block keeps this from matching
    // methods and fields; attributes/modifiers are kept for EF annotations.
    [GeneratedRegex(@"(?<attrs>(?:\[\s*[^\[\]]*\]\s*)*)(?<mods>(?:(?:public|private|protected|internal|static|virtual|override|sealed|required|new|readonly|abstract|unsafe|partial|extern)\s+)*)(?!\b(?:class|record|struct|interface|enum|namespace|return)\b)(?<type>(?:global::)?[A-Za-z_]\w*(?:\s*\.\s*[A-Za-z_]\w*)*(?:\s*<[^;{}()=]*>)?(?:\s*\[\s*\]\s*)*\??)\s+(?<name>[A-Za-z_]\w*)\s*(?<rest>\{\s*(?:(?:public|private|protected|internal)\s+)?(?:get|set|init)\b[^{}]*\})", RegexOptions.Singleline)]
    private static partial Regex PropertyRegex();

    // Matches: interface Name<...> [: base list] { or ;
    [GeneratedRegex(@"\binterface\s+(\w+)(?:<([^>]*)>)?\s*(?:(?!where\b)\s*:\s*([^{;]+?))?\s*(?:\s*where\s+\w+\s*:[^{;]+)?[{;]",
        RegexOptions.Singleline)]
    private static partial Regex InterfaceDeclarationRegex();

    [GeneratedRegex(@"\bDbSet\s*<\s*([^<>]*(?:<[^<>]*>[^<>]*)*)\s*>\s+(\w+)", RegexOptions.Singleline)]
    private static partial Regex DbSetRegex();

    /// <summary>Extracts the first namespace declared in a source file ("" when none).</summary>
    public static string ExtractNamespace(string code) => NamespaceMap.Build(code).FirstNamespace;

    /// <summary>The final segment of a possibly dotted name.</summary>
    public static string SimpleName(string name)
    {
        var idx = name.LastIndexOf('.');
        return idx >= 0 ? name[(idx + 1)..] : name;
    }

    /// <summary>
    /// Internal identity for a source type. Overloaded generic and non-generic
    /// declarations share the same C# name (CommandBase / CommandBase&lt;T&gt;), so
    /// the arity has to be part of identity when resolving references.
    /// </summary>
    internal static string TypeIdentity(string fullName, int arity)
        => arity == 0 ? fullName : $"{fullName}`{arity}";

    /// <summary>Marker name from an internal type identity (drops the `N arity suffix).</summary>
    internal static string SimpleNameFromIdentity(string name)
    {
        var simple = SimpleName(name);
        var tick = simple.LastIndexOf('`');
        return tick > 0 ? simple[..tick] : simple;
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
    /// A primary-constructor base specification carries a base-call argument list
    /// (<c>DbContext(options)</c>, <c>IdentityDbContext&lt;TUser&gt;(options)</c>);
    /// that suffix is not part of the type name and is stripped first.
    /// </summary>
    public static (string Name, List<string> Arguments) ParseTypeReference(string typeReference)
    {
        var text = typeReference.Trim();
        if (text.StartsWith("global::", StringComparison.Ordinal))
            text = text[8..];

        text = StripTrailingArgumentList(text);

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
    /// Removes a trailing parenthesized argument list from a type reference, e.g.
    /// <c>DbContext(options)</c> → <c>DbContext</c> and
    /// <c>IdentityDbContext&lt;AppUser&gt;(options)</c> → <c>IdentityDbContext&lt;AppUser&gt;</c>.
    /// A reference that is itself a tuple type (<c>(int, string)</c>) is left alone:
    /// its opening parenthesis has no type name in front of it.
    /// </summary>
    private static string StripTrailingArgumentList(string text)
    {
        if (!text.EndsWith(')')) return text;

        var depth = 0;
        for (var i = text.Length - 1; i >= 0; i--)
        {
            if (text[i] == ')') depth++;
            else if (text[i] == '(')
            {
                depth--;
                if (depth != 0) continue;
                return i == 0 ? text : text[..i].Trim();
            }
        }
        return text;
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
        var mask = SourceMask.Build(code);
        var namespaces = NamespaceMap.Build(code, mask);
        var scan = new FileScan { Namespace = namespaces.FirstNamespace };
        scan.Usings.AddRange(UsingDirectiveParser.Extract(code, mask, namespaces));

        foreach (Match m in InterfaceDeclarationRegex().Matches(code))
        {
            if (!mask.IsCode(m.Index)) continue;
            var name = m.Groups[1].Value;
            var baseList = m.Groups[3].Value;
            var openBrace = m.Value.EndsWith('{') ? m.Index + m.Length - 1 : -1;
            scan.InterfaceDecls.Add(new InterfaceDecl
            {
                Name = name,
                Namespace = namespaces.NamespaceAt(m.Index),
                BaseList = baseList,
                TypeParameters = ParseTypeParameters(m.Groups[2].Value),
                Usings = scan.Usings,
                BodySpan = ComputeTypeSpan(code, mask, m.Index, openBrace),
            });
            if (!scan.Interfaces.ContainsKey(name))
                scan.Interfaces[name] = new List<string>();
            scan.Interfaces[name].AddRange(ExtractBaseTypeNames(baseList));
        }

        foreach (Match m in DeclarationRegex().Matches(code))
        {
            if (!mask.IsCode(m.Index)) continue;
            var typeName = m.Groups["name"].Value;
            var baseList = m.Groups["base"].Value;
            // The match ends with the declaration's '{' (class with body) or ';'
            // (primary-constructor record/struct without a body).
            var openBrace = m.Value.EndsWith('{') ? m.Index + m.Length - 1 : -1;
            var span = ComputeTypeSpan(code, mask, m.Index, openBrace);
            scan.Classes.Add(new ClassDecl
            {
                Name = typeName,
                Namespace = namespaces.NamespaceAt(m.Index),
                BaseList = baseList,
                BaseTypeNames = ExtractBaseTypeNames(baseList),
                BaseRawNames = ExtractBaseRawNames(baseList),
                TypeParameters = ParseTypeParameters(m.Groups["typeparams"].Value),
                Usings = scan.Usings,
                IsAbstract = IsAbstractDeclaration(code, m.Index),
                BodySpan = span,
                DeclarationIndex = m.Index,
                PrimaryConstructorParameters = m.Groups["params"].Success
                    ? m.Groups["params"].Value
                    : null,
            });
        }

        PopulateContainingTypes(scan);
        PopulateProperties(scan, code, mask);
        return scan;
    }

    private static void PopulateContainingTypes(FileScan scan)
    {
        var containers = scan.Classes
            .Select(c => (Name: c.Name, Span: c.BodySpan, Path: c.ContainingTypes))
            .Concat(scan.InterfaceDecls
                .Select(i => (Name: i.Name, Span: i.BodySpan, Path: i.ContainingTypes)))
            .OrderBy(c => c.Span.Start)
            .ToList();

        foreach (var container in containers)
        {
            var parent = containers
                .Where(p => p.Span.Start < container.Span.Start &&
                            container.Span.Start < p.Span.End)
                .OrderByDescending(p => p.Span.Start)
                .FirstOrDefault();
            if (parent.Name is null) continue;
            container.Path.AddRange(parent.Path);
            container.Path.Add(parent.Name);
        }
    }

    private static void PopulateProperties(FileScan scan, string code, SourceMask mask)
    {
        foreach (var decl in scan.Classes)
        {
            if (decl.BodySpan.End <= decl.BodySpan.Start)
            {
                AddPrimaryConstructorProperties(decl);
                continue;
            }

            var bodyOpen = code.IndexOf('{', decl.BodySpan.Start);
            if (bodyOpen < 0 || bodyOpen >= decl.BodySpan.End)
            {
                AddPrimaryConstructorProperties(decl);
                continue;
            }

            foreach (Match m in PropertyRegex().Matches(code, bodyOpen + 1))
            {
                if (m.Index >= decl.BodySpan.End) break;
                if (!mask.IsCode(m.Index)) continue;

                // A property inside a nested type belongs to the innermost type,
                // not to every containing class.
                var owner = InnermostClass(scan.Classes, m.Index);
                if (!ReferenceEquals(owner, decl)) continue;

                var modifiers = m.Groups["mods"].Value;
                var name = m.Groups["name"].Value;
                if (name == "this") continue;

                var rawType = m.Groups["type"].Value.Trim();
                var rest = m.Groups["rest"].Value;
                var hasSetter = Regex.IsMatch(rest, @"\b(?:set|init)\b");
                if (!hasSetter) continue;
                if (Regex.IsMatch(modifiers, @"\b(?:static|const)\b")) continue;

                var property = new PropertyDecl
                {
                    Name = name,
                    Type = NormalizeTypeSpacing(rawType),
                    IsNullable = EfAnnotations.IsNullable(rawType),
                    HasSetter = true,
                    IsPublic = Regex.IsMatch(modifiers, @"\bpublic\b"),
                    IsStatic = false,
                    SourceIndex = m.Index,
                };
                foreach (Match attribute in Regex.Matches(
                             m.Groups["attrs"].Value, @"\[[^\[\]]*\]"))
                    property.Attributes.Add(attribute.Value);
                decl.Properties.Add(property);
            }

            AddPrimaryConstructorProperties(decl);
        }
    }

    private static ClassDecl? InnermostClass(IReadOnlyList<ClassDecl> classes, int index)
    {
        ClassDecl? owner = null;
        foreach (var candidate in classes)
        {
            if (index < candidate.BodySpan.Start || index >= candidate.BodySpan.End)
                continue;
            if (owner is null || candidate.BodySpan.Start > owner.BodySpan.Start)
                owner = candidate;
        }
        return owner;
    }

    private static void AddPrimaryConstructorProperties(ClassDecl decl)
    {
        if (string.IsNullOrWhiteSpace(decl.PrimaryConstructorParameters)) return;

        foreach (var rawParameter in SplitTopLevel(decl.PrimaryConstructorParameters))
        {
            var parameter = rawParameter.Trim();
            if (parameter.Length == 0) continue;

            var attributes = new List<string>();
            while (parameter.StartsWith('['))
            {
                var close = parameter.IndexOf(']');
                if (close < 0) break;
                attributes.Add(parameter[..(close + 1)]);
                parameter = parameter[(close + 1)..].TrimStart();
            }

            // Default values are not part of the mapped property shape.
            var equals = TopLevelIndexOf(parameter, '=');
            if (equals >= 0) parameter = parameter[..equals].TrimEnd();

            parameter = Regex.Replace(
                parameter, @"^(?:(?:params|in|ref|out|this)\s+)+", "").Trim();
            var match = Regex.Match(parameter, @"^(?<type>.+?)\s+(?<name>@?[A-Za-z_]\w*)$");
            if (!match.Success) continue;

            var name = match.Groups["name"].Value.TrimStart('@');
            if (decl.Properties.Any(p => p.Name == name)) continue;

            var property = new PropertyDecl
            {
                Name = name,
                Type = NormalizeTypeSpacing(match.Groups["type"].Value.Trim()),
                IsNullable = EfAnnotations.IsNullable(match.Groups["type"].Value),
                HasSetter = true,
                IsPublic = true,
                IsStatic = false,
                IsPrimaryConstructorParameter = true,
                SourceIndex = decl.DeclarationIndex,
            };
            property.Attributes.AddRange(attributes);
            decl.Properties.Add(property);
        }
    }

    private static int TopLevelIndexOf(string value, char target)
    {
        var angle = 0;
        var paren = 0;
        for (var i = 0; i < value.Length; i++)
        {
            switch (value[i])
            {
                case '<': angle++; break;
                case '>': if (angle > 0) angle--; break;
                case '(': paren++; break;
                case ')': if (paren > 0) paren--; break;
                case var c when c == target && angle == 0 && paren == 0:
                    return i;
            }
        }
        return -1;
    }

    private static string NormalizeTypeSpacing(string type)
        => Regex.Replace(type, @"\s*([.<>\[\],?])\s*", "$1").Trim();

    /// <summary>
    /// Finds the span covered by a type declaration: from its start to the matching
    /// close brace of its body. Falls back to just the declaration when no body exists.
    /// </summary>
    private static (int Start, int End) ComputeTypeSpan(string code, SourceMask mask, int declStart, int openBrace)
    {
        if (openBrace < 0) return (declStart, declStart);
        var close = mask.FindMatchingBrace(openBrace);
        return (declStart, close < 0 ? code.Length : close + 1);
    }

    /// <summary>
    /// Records namespaces imported by non-alias, non-static usings. The linker
    /// uses them after written/fallback namespaces and before a unique-simple-name
    /// guess, so a handler can point at the contract its file actually imports.
    /// </summary>
    internal static void AddImportedNamespaces(
        IEnumerable<UsingDirective> usings,
        List<string> target)
    {
        foreach (var directive in usings)
        {
            foreach (var ns in directive.NamespaceCandidates())
            {
                if (!target.Contains(ns, StringComparer.Ordinal))
                    target.Add(ns);
            }
        }
    }

    /// <summary>
    /// Turns one file's fragment into analysis entries. DbSets are attributed to the
    /// DbContext class whose body span contains them (supports several contexts per file).
    /// </summary>
    public static void ApplyScan(
        FileScan scan,
        string code,
        string? projectName,
        AnalysisResult result,
        string? filePath = null)
    {
        var contextClasses = new List<(ClassDecl Decl, DbContextInfo Info)>();

        foreach (var decl in scan.Classes)
        {
            var location = SourceLocation.Create(filePath, result.RootPath, code, decl.BodySpan.Start);
            // Open generic / abstract base declarations are not concrete shapes.
            var typeParameters = new HashSet<string>(decl.TypeParameters, StringComparer.Ordinal);
            bool IsConcreteTarget(string? target) =>
                !decl.IsAbstract && target is not null && !typeParameters.Contains(target);

            // IRequest<...> / IRequest  -> a CQRS request
            if (!decl.IsAbstract && decl.BaseRawNames.Contains("IRequest"))
            {
                var response = ExtractGenericArguments(decl.BaseList, "IRequest")?.FirstOrDefault();
                var request = new CqrsRequest
                {
                    Name = decl.Name,
                    Namespace = decl.Namespace,
                    ResponseType = response,
                    ProjectName = projectName,
                    Location = location,
                };
                request.ContainingTypes.AddRange(decl.ContainingTypes);
                result.Requests.Add(request);
            }

            // INotification / IDomainEvent -> a CQRS notification / domain event
            if (!decl.IsAbstract &&
                (decl.BaseRawNames.Contains("INotification") || decl.BaseRawNames.Contains("IDomainEvent")))
            {
                if (!result.Notifications.Any(n =>
                        n.Name == decl.Name &&
                        n.Namespace == decl.Namespace &&
                        n.ProjectName == projectName &&
                        n.ContainingTypes.SequenceEqual(decl.ContainingTypes)))
                {
                    var notification = new CqrsNotification
                    {
                        Name = decl.Name,
                        Namespace = decl.Namespace,
                        ProjectName = projectName,
                        ViaMarker = decl.BaseRawNames.Contains("INotification") ? null : "IDomainEvent",
                        Location = location,
                    };
                    notification.ContainingTypes.AddRange(decl.ContainingTypes);
                    result.Notifications.Add(notification);
                }
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
                        h.ProjectName == projectName &&
                        h.ContainingTypes.SequenceEqual(decl.ContainingTypes) &&
                        h.RequestName == UnqualifiedTypeName(requestType)))
                    continue;

                var handler = new HandlerInfo
                {
                    Name = decl.Name,
                    Namespace = decl.Namespace,
                    RequestName = UnqualifiedTypeName(requestType),
                    RequestNamespace = NamespaceOfType(requestType) is { Length: > 0 } ns ? ns : null,
                    ProjectName = projectName,
                    Location = location,
                };
                handler.ContainingTypes.AddRange(decl.ContainingTypes);
                AddImportedNamespaces(decl.Usings, handler.ImportedNamespaces);
                result.Handlers.Add(handler);
            }

            // INotificationHandler<TNotification> / IDomainEventHandler<TNotification> -> a notification handler
            var notificationTargets = new List<string>();
            foreach (var root in new[] { "INotificationHandler", "IDomainEventHandler" })
                notificationTargets.AddRange(ExtractAllGenericArguments(decl.BaseList, root)
                    .Where(a => a.Count >= 1)
                    .Select(a => a[0]));
            foreach (var notificationType in notificationTargets.Distinct(StringComparer.Ordinal))
            {
                if (!IsConcreteTarget(UnqualifiedTypeName(notificationType))) continue;
                if (result.NotificationHandlers.Any(h =>
                        h.Namespace == decl.Namespace &&
                        h.Name == decl.Name &&
                        h.ProjectName == projectName &&
                        h.ContainingTypes.SequenceEqual(decl.ContainingTypes) &&
                        h.NotificationName == UnqualifiedTypeName(notificationType)))
                    continue;

                var notificationHandler = new NotificationHandlerInfo
                {
                    Name = decl.Name,
                    Namespace = decl.Namespace,
                    NotificationName = UnqualifiedTypeName(notificationType),
                    NotificationNamespace = NamespaceOfType(notificationType) is { Length: > 0 } ns ? ns : null,
                    ProjectName = projectName,
                    Location = location,
                };
                notificationHandler.ContainingTypes.AddRange(decl.ContainingTypes);
                AddImportedNamespaces(decl.Usings, notificationHandler.ImportedNamespaces);
                result.NotificationHandlers.Add(notificationHandler);
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
                        v.ProjectName == projectName &&
                        v.ContainingTypes.SequenceEqual(decl.ContainingTypes) &&
                        v.TargetType == UnqualifiedTypeName(validatorTarget)))
                    continue;

                var validator = new ValidatorInfo
                {
                    Name = decl.Name,
                    Namespace = decl.Namespace,
                    TargetType = UnqualifiedTypeName(validatorTarget),
                    TargetNamespace = NamespaceOfType(validatorTarget) is { Length: > 0 } ns ? ns : null,
                    ProjectName = projectName,
                    Location = location,
                };
                validator.ContainingTypes.AddRange(decl.ContainingTypes);
                AddImportedNamespaces(decl.Usings, validator.ImportedNamespaces);
                result.Validators.Add(validator);
            }

            // IPipelineBehavior<...> -> a pipeline behavior
            if (decl.BaseList.Contains("IPipelineBehavior<", StringComparison.Ordinal))
            {
                result.Behaviors.Add(new BehaviorInfo
                {
                    Name = decl.Name,
                    ProjectName = projectName,
                    Location = location,
                });
            }

            // : DbContext (or *DbContext, e.g. IdentityDbContext) -> a DbContext
            if (!decl.IsAbstract && IsDbContextBase(decl.BaseList))
            {
                var ctx = new DbContextInfo
                {
                    Name = decl.Name,
                    Namespace = decl.Namespace,
                    ProjectName = projectName,
                    Location = location,
                };
                ctx.ContainingTypes.AddRange(decl.ContainingTypes);
                contextClasses.Add((decl, ctx));
                result.DbContexts.Add(ctx);
            }
        }

        if (contextClasses.Count > 0)
        {
            var mask = SourceMask.Build(code);
            foreach (Match m in DbSetRegex().Matches(code))
            {
                if (!mask.IsCode(m.Index)) continue;
                var owner = contextClasses
                    .Where(c => m.Index >= c.Decl.BodySpan.Start && m.Index < c.Decl.BodySpan.End)
                    .OrderByDescending(c => c.Decl.BodySpan.Start)
                    .FirstOrDefault();
                if (owner.Info is null) continue;
                var set = new DbSetInfo(m.Groups[1].Value.Trim(), m.Groups[2].Value);
                if (!owner.Info.DbSets.Contains(set))
                    owner.Info.DbSets.Add(set);
            }
        }
    }

    /// <summary>
    /// Recognizes DbContext base types by their final type name. Fully-qualified
    /// <c>Microsoft.EntityFrameworkCore.DbContext</c> and custom <c>*DbContext</c> bases
    /// count, including primary-constructor base calls such as <c>DbContext(options)</c>;
    /// interface-like <c>IDbContext</c>/<c>IShopDbContext</c> names do not.
    /// </summary>
    public static bool IsDbContextBase(string baseList)
    {
        foreach (var part in SplitTopLevel(baseList))
        {
            var (name, _) = ParseTypeReference(part);
            var simple = SimpleName(name);
            if (simple.Length == 0) continue;

            // IShopDbContext / IDbContext are interfaces, not concrete context bases.
            if (simple.Length > 1 && simple[0] == 'I' && char.IsUpper(simple[1]) &&
                simple.EndsWith("DbContext", StringComparison.Ordinal))
                continue;

            if (simple == "DbContext" || simple.EndsWith("DbContext", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Convenience for tests and single-file scans: scan text directly into a result.
    /// (v0.1-compatible entry point; files scanned this way get no marker resolution.)
    /// </summary>
    public static void ScanText(
        string code,
        string? projectName,
        AnalysisResult result,
        string? filePath = null)
    {
        var scan = ScanText(code);
        ApplyScan(scan, code, projectName, result, filePath);
        var file = new SourceFile(filePath ?? "", code, projectName);
        var hierarchy = new TypeHierarchy(scan.Classes, scan.InterfaceDecls);
        EfCoreTextModelBuilder.Build(new[] { (file, scan) }, hierarchy, result);
    }

    public static void ScanFile(string path, string? projectName, AnalysisResult result)
        => ScanText(File.ReadAllText(path), projectName, result, path);

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
            // Parentheses and brackets nest too: a primary-constructor base call
            // (DbContext(options, flag)) or a tuple/array type argument must not
            // be split at its inner commas.
            if (c is '<' or '(' or '[') depth++;
            if (c is '>' or ')' or ']') depth--;
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
