using System.Text.RegularExpressions;
using static CqrsLens.DeepSlicePatterns;

namespace CqrsLens;

/// <summary>
/// Resolves which type a member-access receiver refers to. Declarations are read from the
/// enclosing class; <c>var</c> locals are inferred only when the initializer makes the type
/// unambiguous. Unresolvable or conflicting identifiers are deliberately omitted rather than
/// guessed, because a wrong receiver would attribute database work or events to the wrong entity.
/// </summary>
internal static partial class ReceiverTypeMapBuilder
{
    [GeneratedRegex(@"(?<![\w@.])(?<type>(?:global::)?[A-Za-z_]\w*(?:\s*\.\s*[A-Za-z_]\w*)*(?:\s*<[^;{}()=]*?>)?(?:\s*\[\s*\])*\??)\s+(?<name>@?[A-Za-z_]\w*)\s*(?=[;=,)])")]
    private static partial Regex TypedDeclarationRegex();

    [GeneratedRegex(@"\bvar\s+(?<name>@?[A-Za-z_]\w*)\s*=\s*(?<init>[^;]*)")]
    private static partial Regex VarDeclarationRegex();

    [GeneratedRegex(@"^new\s+(?<type>(?:global::)?[A-Za-z_]\w*(?:\s*\.\s*[A-Za-z_]\w*)*)\s*[\(\{<]")]
    private static partial Regex LeadingObjectCreationRegex();

    [GeneratedRegex(@"^(?<receiver>@?[A-Za-z_]\w*)\s*[!?]?\s*\.\s*[A-Za-z_]\w*")]
    private static partial Regex LeadingMemberCallRegex();

    [GeneratedRegex(@"^T(?:[A-Z]\w*)?$")]
    private static partial Regex TypeParameterNameRegex();

    /// <summary>Words the typed-declaration regex can capture in the "type" slot that are not types.</summary>
    private static readonly HashSet<string> NonTypeWords = new(StringComparer.Ordinal)
    {
        "return", "await", "throw", "new", "else", "in", "out", "ref", "yield", "case", "goto", "using",
        "is", "as", "not", "and", "or", "when", "var", "typeof", "nameof", "default", "const", "static",
        "readonly", "public", "private", "protected", "internal", "async", "base", "this", "select", "from",
        "where", "let", "orderby", "group", "into", "join", "on", "equals", "by", "class", "record", "struct",
        "interface", "enum", "namespace", "params", "sealed", "virtual", "override", "abstract", "partial",
        "required", "volatile", "unsafe", "fixed", "lock", "checked", "unchecked", "operator", "implicit",
        "explicit", "event", "delegate", "get", "set", "init", "add", "remove", "value", "global",
    };

    internal static Dictionary<string, string> Build(
        CSharpScanner.ClassDecl decl,
        string code,
        SourceMask mask,
        List<CSharpScanner.ClassDecl> classes,
        Dictionary<string, string> dbSetsByProperty,
        HashSet<string> dbContextTypeNames,
        Dictionary<string, List<string>> repoEntitiesByType)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var conflictingNames = new HashSet<string>(StringComparer.Ordinal);

        void AddDeclared(string type, string name)
        {
            type = Regex.Replace(type.Trim(), @"\s+", "");
            name = name.TrimStart('@');
            if (type.Length == 0 || name.Length == 0 || NonTypeWords.Contains(type))
                return;
            if (conflictingNames.Contains(name))
                return;

            if (map.TryGetValue(name, out var existing))
            {
                // The same identifier reused in sibling scopes can hold different types.
                // Resolving either one would be a guess, so drop the name entirely.
                if (!string.Equals(existing, type, StringComparison.Ordinal))
                {
                    map.Remove(name);
                    conflictingNames.Add(name);
                }
                return;
            }

            map[name] = type;
        }

        if (!string.IsNullOrWhiteSpace(decl.PrimaryConstructorParameters))
        {
            foreach (var raw in CSharpScanner.SplitTopLevel(decl.PrimaryConstructorParameters))
            {
                var type = DeepSliceSyntax.ExtractParameterType(raw);
                var name = DeepSliceSyntax.ExtractParameterName(raw);
                if (type is not null && name is not null)
                    AddDeclared(type, name);
            }
        }

        if (decl.BodySpan.End <= decl.BodySpan.Start)
            return map;

        var bodyStart = decl.BodySpan.Start;
        var bodyEnd = Math.Min(code.Length, decl.BodySpan.End);

        foreach (Match m in TypedDeclarationRegex().Matches(code, bodyStart))
        {
            if (m.Index >= bodyEnd) break;
            if (NonTypeWords.Contains(m.Groups["type"].Value.Trim())) continue;
            if (!mask.IsCode(m.Index)) continue;
            if (!ReferenceEquals(DeepSliceSyntax.InnermostClass(classes, m.Index), decl)) continue;
            AddDeclared(m.Groups["type"].Value, m.Groups["name"].Value);
        }

        foreach (Match m in VarDeclarationRegex().Matches(code, bodyStart))
        {
            if (m.Index >= bodyEnd) break;
            if (!mask.IsCode(m.Index)) continue;
            if (!ReferenceEquals(DeepSliceSyntax.InnermostClass(classes, m.Index), decl)) continue;

            var inferred = InferInitializerType(m.Groups["init"].Value, map, dbSetsByProperty, dbContextTypeNames, repoEntitiesByType);
            if (inferred is not null)
                AddDeclared(inferred, m.Groups["name"].Value);
        }

        return map;
    }

    private static string? InferInitializerType(
        string initializer,
        Dictionary<string, string> receiverTypes,
        Dictionary<string, string> dbSetsByProperty,
        HashSet<string> dbContextTypeNames,
        Dictionary<string, List<string>> repoEntitiesByType)
    {
        var s = initializer.Trim();
        if (s.StartsWith("await ", StringComparison.Ordinal))
            s = s[6..].TrimStart();

        var created = LeadingObjectCreationRegex().Match(s);
        if (created.Success)
            return CSharpScanner.UnqualifiedTypeName(created.Groups["type"].Value);

        var set = DbContextSetMethodRegex().Match(s);
        if (set.Success)
        {
            if (!IsDbContextReceiver(GetReceiverIdentifier(s, set.Index), receiverTypes, dbContextTypeNames))
                return null;

            var setEntity = CSharpScanner.SimpleName(set.Groups["entity"].Value.Trim());
            return TypeParameterNameRegex().IsMatch(setEntity) ? null : setEntity;
        }

        foreach (Match m in MemberAccessRegex().Matches(s))
        {
            if (dbSetsByProperty.TryGetValue(m.Groups["prop"].Value, out var label) &&
                IsDbContextReceiver(GetReceiverIdentifier(s, m.Index), receiverTypes, dbContextTypeNames))
            {
                return EntityFromLabel(label);
            }
        }

        var call = LeadingMemberCallRegex().Match(s);
        if (!call.Success)
            return null;

        var receiver = call.Groups["receiver"].Value.TrimStart('@');
        if (receiverTypes.TryGetValue(receiver, out var receiverType))
            return RepositoryEntity(receiverType, repoEntitiesByType);

        // Static factory on a type (`Order.Create(..)`) conventionally returns that type.
        return char.IsUpper(receiver[0]) ? receiver : null;
    }

    /// <summary>Entity returned by a repository-typed receiver, or null when it is not a repository.</summary>
    internal static string? RepositoryEntity(string repositoryType, Dictionary<string, List<string>> repoEntitiesByType)
    {
        var generic = GenericRepositoryRegex().Match(repositoryType);
        if (generic.Success)
            return CSharpScanner.SimpleName(generic.Groups["entity"].Value.Trim());

        var simple = CSharpScanner.UnqualifiedTypeName(repositoryType);
        var named = NamedRepositoryRegex().Match(simple);
        if (named.Success)
            return named.Groups["entity"].Value;

        return repoEntitiesByType.TryGetValue(simple, out var labels) && labels.Count == 1
            ? EntityFromLabel(labels[0])
            : null;
    }

    /// <summary>
    /// Candidate types for a member-access receiver: its declared or inferred type only. A name
    /// heuristic such as <c>order</c> -> <c>Order</c> is deliberately not used, because a local
    /// named after one entity can hold another (<c>var order = customer.Orders[0]</c>).
    /// </summary>
    internal static IEnumerable<string> ResolveReceiverTypes(
        string? receiver,
        Dictionary<string, string> receiverTypes)
    {
        if (receiver is null or "this" or "base")
            yield break;

        if (receiverTypes.TryGetValue(receiver, out var declared))
            yield return CSharpScanner.UnqualifiedTypeName(declared);
    }

    /// <summary>
    /// True when a DbSet-named member access is made on a DbContext: an identifier typed as a known
    /// DbContext / <c>*Context</c> / <c>*Db</c>, or (when untyped, e.g. an inherited member) named like one.
    /// </summary>
    internal static bool IsDbContextReceiver(
        string? receiver,
        Dictionary<string, string> receiverTypes,
        HashSet<string> dbContextTypeNames)
    {
        if (receiver is null or "this" or "base")
            return false;

        if (receiverTypes.TryGetValue(receiver, out var declared))
        {
            var simple = CSharpScanner.UnqualifiedTypeName(declared);
            return dbContextTypeNames.Contains(simple) ||
                   simple.EndsWith("Context", StringComparison.Ordinal) ||
                   simple.EndsWith("Db", StringComparison.Ordinal);
        }

        var bare = receiver.TrimStart('_').ToLowerInvariant();
        return bare == "db" ||
               bare == "context" ||
               bare.EndsWith("context", StringComparison.Ordinal) ||
               bare.EndsWith("db", StringComparison.Ordinal);
    }

    /// <summary>
    /// The identifier immediately left of the member-access dot at <paramref name="dotIndex"/>
    /// (<c>order</c> in <c>order!.Update()</c>), or null when the receiver is an expression
    /// such as a call result or indexer.
    /// </summary>
    internal static string? GetReceiverIdentifier(string code, int dotIndex)
    {
        var i = dotIndex - 1;
        while (i >= 0 && (char.IsWhiteSpace(code[i]) || code[i] is '!' or '?'))
            i--;

        var end = i + 1;
        while (i >= 0 && (char.IsLetterOrDigit(code[i]) || code[i] is '_' or '@'))
            i--;

        var ident = code[(i + 1)..end].TrimStart('@');
        return ident.Length == 0 || char.IsDigit(ident[0]) ? null : ident;
    }

    /// <summary>
    /// Rejects generic type parameters (<c>T</c>, <c>TEntity</c>) masquerading as entities when a
    /// generic repository such as <c>EfRepository&lt;T&gt; : IRepository&lt;T&gt;</c> is scanned.
    /// </summary>
    internal static bool IsEntityCandidate(
        string entitySimple,
        CSharpScanner.ClassDecl decl,
        Dictionary<string, string> dbSetsByEntity)
    {
        if (string.IsNullOrEmpty(entitySimple))
            return false;
        if (decl.TypeParameters.Contains(entitySimple, StringComparer.Ordinal))
            return false;
        return dbSetsByEntity.ContainsKey(entitySimple) || !TypeParameterNameRegex().IsMatch(entitySimple);
    }

    /// <summary>Interface-style (<c>IName</c>) base types of a class that take no type arguments.</summary>
    internal static List<string> NonGenericInterfaceNames(CSharpScanner.ClassDecl decl)
        => CSharpScanner.SplitTopLevel(decl.BaseList)
            .Where(bt => !bt.Contains('<'))
            .Select(bt => CSharpScanner.UnqualifiedTypeName(bt))
            .Where(name => name.Length > 1 && name[0] == 'I' && char.IsUpper(name[1]))
            .ToList();

    internal static string EntityFromLabel(string label)
    {
        var openParen = label.IndexOf('(');
        var closeParen = label.LastIndexOf(')');
        return openParen >= 0 && closeParen > openParen + 1
            ? label[(openParen + 1)..closeParen].Trim()
            : label.Trim();
    }
}
