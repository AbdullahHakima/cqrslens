using System.Text.RegularExpressions;

namespace CqrsLens;

/// <summary>
/// Scans non-test source files for published integration events, domain events, and topic
/// messages across handlers, integration-event services, workers, and endpoints.
/// </summary>
internal static partial class SystemIntegrationEventScanner
{
    [GeneratedRegex(
        @"(?<hasDot>\.\s*)?(?<method>PublishThroughEventBusAsync|SaveAndPublishEventAsync|AddAndSaveEventAsync|PublishEventAsync|PublishAsync|Publish|SendAsync|Send|EnqueueAsync|AddDomainEvent|RaiseDomainEvent|QueueDomainEvent)\s*(?:<\s*(?<generic>(?:global::)?[A-Za-z_]\w*(?:\s*\.\s*[A-Za-z_]\w*)*)\s*(?:,|>))?\s*\(",
        RegexOptions.None,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex PublishCallRegex();

    [GeneratedRegex(
        @"^new\s+(?:global::)?(?<type>[A-Za-z_]\w*(?:\s*\.\s*[A-Za-z_]\w*)*)\s*[\(\{<]",
        RegexOptions.None,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex LeadingObjectCreationRegex();

    [GeneratedRegex(
        @"^@?[A-Za-z_]\w*(?:\s*\.\s*@?[A-Za-z_]\w*)*$",
        RegexOptions.None,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex SimpleIdentifierRegex();

    [GeneratedRegex(
        @"(?<![\w@.])(?<type>(?:global::)?[A-Za-z_]\w*(?:\s*\.\s*[A-Za-z_]\w*)*(?:\s*<[^;{}()=]*?>)?(?:\s*\[\s*\])*\??)\s+(?<name>@?[A-Za-z_]\w*)\s*(?=[;=,)])",
        RegexOptions.None,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex TypedDeclarationRegex();

    [GeneratedRegex(
        @"\bvar\s+(?<name>@?[A-Za-z_]\w*)\s*=\s*(?<init>[^;]*)",
        RegexOptions.None,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex VarDeclarationRegex();

    private static readonly HashSet<string> IgnoredEventTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "object", "string", "bool", "byte", "short", "int", "long", "float", "double", "decimal",
        "char", "Guid", "DateTime", "DateTimeOffset", "TimeSpan", "CancellationToken", "Task",
        "ValueTask", "dynamic", "var", "Exception", "EventArgs", "IntegrationEvent", "DomainEvent",
        "INotification", "IEvent", "IIntegrationEvent", "IDomainEvent", "IMessage", "IRequest",
        "ICommand", "IQuery", "Type", "JsonElement", "JsonNode", "JsonDocument", "MemoryStream",
    };

    private static readonly HashSet<string> NonTypeWords = new(StringComparer.Ordinal)
    {
        "return", "await", "throw", "new", "else", "in", "out", "ref", "yield", "case", "goto", "using",
        "is", "as", "not", "and", "or", "when", "var", "typeof", "nameof", "default", "const", "static",
        "readonly", "public", "private", "protected", "internal", "async", "base", "this", "select", "from",
        "where", "let", "orderby", "group", "into", "join", "on", "equals", "by", "class", "record", "struct",
        "interface", "enum", "namespace", "params", "sealed", "virtual", "override", "abstract", "partial",
        "required", "volatile", "unsafe", "fixed", "lock", "checked", "unchecked",
    };

    internal static IReadOnlyList<PublishedEventHit> ScanAll(SystemAnalysisContext context)
    {
        var hits = new List<PublishedEventHit>();
        var seen = new HashSet<(string Project, string File, int Offset, string Event)>();

        foreach (var (file, scan, mask) in context.ParsedFiles)
        {
            if (context.IsTestProject(file.ProjectName))
                continue;
            if (!MightContainPublishCall(file.Code))
                continue;

            var projectName = file.ProjectName ?? string.Empty;

            try
            {
                foreach (Match match in PublishCallRegex().Matches(file.Code))
                {
                    if (!mask.IsCode(match.Index))
                        continue;

                    var method = match.Groups["method"].Value;
                    var isDomainMethod = method is "AddDomainEvent" or "RaiseDomainEvent" or "QueueDomainEvent";
                    if (!isDomainMethod && !match.Groups["hasDot"].Success)
                        continue;

                    if (IsMethodDeclarationHeader(file.Code, mask, match.Index))
                        continue;

                    var decl = DeepSliceSyntax.InnermostClass(scan.Classes, match.Index);
                    if (decl is not null && decl.BodySpan.End <= decl.BodySpan.Start)
                        continue;

                    var senderFullName = decl?.FullName ?? Path.GetFileNameWithoutExtension(file.Path);
                    var openParen = match.Index + match.Length - 1;
                    var firstArg = ExtractFirstArgument(file.Code, mask, openParen + 1);

                    // Topic publish (e.g. CAP: _capBus.PublishAsync("orders.created", payload))
                    if (!isDomainMethod &&
                        method is "Publish" or "PublishAsync" or "Send" or "SendAsync" &&
                        TryExtractStringLiteral(firstArg, out var topic) &&
                        !string.IsNullOrWhiteSpace(topic))
                    {
                        if (seen.Add((projectName, file.Path, match.Index, topic)))
                        {
                            hits.Add(new PublishedEventHit(
                                projectName,
                                file.Path,
                                match.Index,
                                senderFullName,
                                topic,
                                topic,
                                null,
                                IsTopic: true,
                                SourceLocation.Create(file.Path, context.Result.RootPath, file.Code, match.Index)));
                        }
                        continue;
                    }

                    string? rawType;
                    if (match.Groups["generic"].Success && match.Groups["generic"].Value.Length > 0)
                    {
                        rawType = match.Groups["generic"].Value;
                    }
                    else
                    {
                        var (scopeStart, scopeEnd) = FindLocalScopeBounds(file.Code, mask, decl, match.Index);
                        Dictionary<string, string>? fieldMap = decl is not null
                            ? context.GetClassMemberMap(decl, file.Code, mask, scan.Classes)
                            : null;

                        var localMap = BuildScopeVariableMap(file.Code, mask, scopeStart, scopeEnd, fieldMap, alreadyMasked: false);
                        rawType = ResolveArgumentType(firstArg, localMap);
                    }

                    if (string.IsNullOrWhiteSpace(rawType))
                        continue;

                    var (parsedName, parsedArgs) = CSharpScanner.ParseTypeReference(rawType);
                    if (parsedName.Length == 0)
                        continue;

                    var simple = CSharpScanner.UnqualifiedTypeName(parsedName);
                    if (simple.Length == 0 || IgnoredEventTypes.Contains(simple))
                        continue;
                    if (decl is not null && decl.TypeParameters.Contains(simple, StringComparer.Ordinal))
                        continue;

                    var currentNs = decl?.Namespace ?? scan.Namespace;
                    IReadOnlyList<string> containing = decl?.ContainingTypes ?? (IReadOnlyList<string>)Array.Empty<string>();
                    var resolvedClass = context.Hierarchy.TryResolveClass(
                        parsedName, currentNs, scan.Usings, containing, parsedArgs.Count, out _);
                    if (resolvedClass is not null && resolvedClass.IsAbstract)
                        continue;

                    var resolvedFullName = resolvedClass?.FullName;
                    if (method is "Send" or "SendAsync")
                    {
                        var isKnownRequest = (resolvedFullName is not null && context.RequestsByFullName.ContainsKey(resolvedFullName))
                            || (resolvedFullName is null && context.RequestsBySimple.ContainsKey(simple));
                        if (isKnownRequest)
                            continue;
                    }

                    var eventName = resolvedFullName ?? simple;
                    if (!seen.Add((projectName, file.Path, match.Index, eventName)))
                        continue;

                    hits.Add(new PublishedEventHit(
                        projectName,
                        file.Path,
                        match.Index,
                        senderFullName,
                        eventName,
                        simple,
                        resolvedFullName,
                        IsTopic: false,
                        SourceLocation.Create(file.Path, context.Result.RootPath, file.Code, match.Index)));
                }
            }
            catch (RegexMatchTimeoutException)
            {
            }
        }

        return hits;
    }

    internal static Dictionary<string, string> BuildClassMemberMap(
        CSharpScanner.ClassDecl decl,
        string code,
        SourceMask mask,
        List<CSharpScanner.ClassDecl> classes)
    {
        var members = new List<(string Type, string Name)>();
        DependencyExtractor.ExtractParameters(decl, code, mask, classes, members);
        ReceiverTypeMapBuilder.CollectClassLevelDeclarations(decl, code, mask, classes, members);
        foreach (var prop in decl.Properties)
        {
            if (prop.Name.Length > 0 && prop.Type.Length > 0)
                members.Add((prop.Type, prop.Name));
        }

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (type, name) in members)
        {
            var trimmedName = name.TrimStart('@');
            if (trimmedName.Length > 0 && type.Length > 0)
                map.TryAdd(trimmedName, type);
        }
        return map;
    }

    internal static Dictionary<string, string> BuildScopeVariableMap(
        string code,
        SourceMask mask,
        int scopeStart,
        int scopeEnd,
        IReadOnlyDictionary<string, string>? baseMembers,
        bool alreadyMasked = false)
    {
        var map = baseMembers is not null
            ? new Dictionary<string, string>(baseMembers, StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);

        if (scopeEnd <= scopeStart || scopeStart < 0 || scopeEnd > code.Length)
            return map;

        var slice = alreadyMasked && scopeStart == 0 && scopeEnd == code.Length
            ? code
            : mask.CodeOnly(scopeStart, scopeEnd - scopeStart);

        try
        {
            foreach (Match m in TypedDeclarationRegex().Matches(slice))
            {
                var rawType = m.Groups["type"].Value.Trim();
                if (rawType.Length == 0 || NonTypeWords.Contains(rawType))
                    continue;
                var name = m.Groups["name"].Value.TrimStart('@');
                if (name.Length > 0)
                    map[name] = NormalizeTypeSpacing(rawType);
            }

            foreach (Match m in VarDeclarationRegex().Matches(slice))
            {
                var name = m.Groups["name"].Value.TrimStart('@');
                if (name.Length == 0)
                    continue;
                var init = m.Groups["init"].Value.Trim();
                if (init.StartsWith("await ", StringComparison.Ordinal))
                    init = init[6..].TrimStart();
                var created = LeadingObjectCreationRegex().Match(init);
                if (created.Success)
                {
                    var createdType = NormalizeTypeSpacing(created.Groups["type"].Value);
                    if (createdType.Length > 0)
                        map[name] = createdType;
                }
            }
        }
        catch (RegexMatchTimeoutException)
        {
        }

        return map;
    }

    private static (int Start, int End) FindLocalScopeBounds(
        string code,
        SourceMask mask,
        CSharpScanner.ClassDecl? decl,
        int index)
    {
        if (decl is null)
            return (0, code.Length);

        var bodyEnd = Math.Min(code.Length, decl.BodySpan.End);
        var openBrace = code.IndexOf('{', decl.BodySpan.Start);
        if (openBrace < 0 || openBrace >= bodyEnd)
            return (decl.BodySpan.Start, bodyEnd);

        var closeBrace = mask.FindMatchingBrace(openBrace);
        var limit = closeBrace > openBrace ? Math.Min(bodyEnd, closeBrace) : bodyEnd;

        var memberStart = openBrace + 1;
        for (var i = openBrace + 1; i < limit; i++)
        {
            if (!mask.IsCode(i))
                continue;

            var c = code[i];
            if (c == '(')
            {
                var closeParen = DeepSliceSyntax.FindMatchingParen(code, mask, i);
                if (closeParen > i)
                    i = closeParen;
                continue;
            }

            if (c == '{')
            {
                var blockClose = mask.FindMatchingBrace(i);
                if (blockClose <= i)
                    break;

                if (index >= memberStart && index <= blockClose)
                    return (memberStart, Math.Min(limit, blockClose + 1));

                i = blockClose;
                memberStart = blockClose + 1;
            }
            else if (c == ';')
            {
                if (index >= memberStart && index <= i)
                    return (memberStart, Math.Min(limit, i + 1));
                memberStart = i + 1;
            }
        }

        return (decl.BodySpan.Start, bodyEnd);
    }

    private static string? ResolveArgumentType(string argument, Dictionary<string, string> localMap)
    {
        var arg = argument.Trim();
        if (arg.Length == 0)
            return null;

        try
        {
            var created = LeadingObjectCreationRegex().Match(arg);
            if (created.Success)
                return NormalizeTypeSpacing(created.Groups["type"].Value);

            if (SimpleIdentifierRegex().IsMatch(arg))
            {
                var name = arg.TrimStart('@');
                if (localMap.TryGetValue(name, out var declared) && declared.Length > 0)
                    return declared;
            }
        }
        catch (RegexMatchTimeoutException)
        {
        }

        return null;
    }

    private static bool TryExtractStringLiteral(string rawArg, out string value)
    {
        value = string.Empty;
        var trimmed = rawArg.Trim();
        if (trimmed.Length < 2)
            return false;

        if (trimmed.StartsWith("@\"", StringComparison.Ordinal) && trimmed.EndsWith('"') && trimmed.Length >= 3)
        {
            value = trimmed[2..^1].Replace("\"\"", "\"", StringComparison.Ordinal);
            return value.Length <= 200;
        }

        if (trimmed[0] == '"' && trimmed[^1] == '"' && trimmed.IndexOf('"', 1) == trimmed.Length - 1)
        {
            value = trimmed[1..^1];
            return value.Length <= 200;
        }

        return false;
    }

    private static string ExtractFirstArgument(string code, SourceMask mask, int start)
    {
        var depth = 0;
        var i = start;
        for (; i < code.Length; i++)
        {
            if (!mask.IsCode(i))
                continue;

            var c = code[i];
            if (c is '(' or '[' or '{')
            {
                depth++;
            }
            else if (c is ')' or ']' or '}')
            {
                if (c == ')' && depth == 0)
                    break;
                if (depth > 0)
                    depth--;
            }
            else if (depth == 0 && c is ',' or ';')
            {
                break;
            }
        }

        return i > start ? code[start..i] : string.Empty;
    }

    private static bool IsMethodDeclarationHeader(string code, SourceMask mask, int methodIndex)
    {
        var start = Math.Max(0, methodIndex - 120);
        for (var i = methodIndex - 1; i >= start; i--)
        {
            if (!mask.IsCode(i))
                continue;
            if (code[i] is ';' or '{' or '}' or '=')
            {
                start = i + 1;
                break;
            }
        }

        var prefix = mask.CodeOnly(start, methodIndex - start).Trim();
        if (prefix.Length == 0)
            return false;

        // A declaration header before MethodName( has modifier/return-type tokens ending in a space, not `.` or `await`.
        if (prefix.EndsWith('.'))
            return false;

        return prefix.Contains("void ", StringComparison.Ordinal) ||
               prefix.Contains("Task ", StringComparison.Ordinal) ||
               prefix.Contains("Task<", StringComparison.Ordinal) ||
               prefix.Contains("ValueTask", StringComparison.Ordinal) ||
               prefix.StartsWith("public ", StringComparison.Ordinal) ||
               prefix.StartsWith("private ", StringComparison.Ordinal) ||
               prefix.StartsWith("protected ", StringComparison.Ordinal) ||
               prefix.StartsWith("internal ", StringComparison.Ordinal);
    }

    private static bool MightContainPublishCall(string code)
        => code.Contains("Publish", StringComparison.Ordinal) ||
           code.Contains(".Send", StringComparison.Ordinal) ||
           code.Contains("AddAndSaveEventAsync", StringComparison.Ordinal) ||
           code.Contains("EnqueueAsync", StringComparison.Ordinal) ||
           code.Contains("DomainEvent", StringComparison.Ordinal);

    private static string NormalizeTypeSpacing(string type)
    {
        try
        {
            return Regex.Replace(type.Trim(), @"\s+", "", RegexOptions.None, TimeSpan.FromSeconds(2));
        }
        catch (RegexMatchTimeoutException)
        {
            return type.Trim();
        }
    }
}
