using System.Text.RegularExpressions;

namespace CqrsLens;

/// <summary>
/// Cached per-class member map (including inherited base-class fields and properties)
/// and intra-class helper method bodies for method-scoped entrypoint call slicing.
/// </summary>
internal sealed partial class ClassMethodIndex
{
    private const int MaxBaseDepth = 4;
    private const int MaxHelperMethods = 200;
    private const int MaxHelperHops = 2;

    [GeneratedRegex(
        @"(?<attrs>(?:\[\s*[^\[\]]*\]\s*)*)(?:(?:public|protected|internal|private|static|virtual|override|async|sealed|new)\s+)+[^\(\{;=]+?\s+(?<method>[A-Za-z_]\w*)\s*(?:<[^(){};=]*>)?\s*\(",
        RegexOptions.Singleline,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex MemberMethodRegex();

    [GeneratedRegex(
        @"\[(?:Http(?:Get|Post|Put|Delete|Patch)|Route|CapSubscribe)\b",
        RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex EndpointMethodAttrRegex();

    [GeneratedRegex(
        @"\[NonAction\b",
        RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex NonActionAttrRegex();

    private ClassMethodIndex(
        IReadOnlyDictionary<string, string> memberTypes,
        IReadOnlyDictionary<string, List<(string Body, int Start, int End)>> helperMethodsByName)
    {
        MemberTypes = memberTypes;
        HelperMethodsByName = helperMethodsByName;
    }

    public IReadOnlyDictionary<string, string> MemberTypes { get; }

    public IReadOnlyDictionary<string, List<(string Body, int Start, int End)>> HelperMethodsByName { get; }

    internal static ClassMethodIndex Build(
        SystemAnalysisContext context,
        CSharpScanner.ClassDecl decl,
        SourceFile file,
        CSharpScanner.FileScan scan,
        SourceMask mask)
    {
        var memberTypes = new Dictionary<string, string>(
            context.GetClassMemberMap(decl, file.Code, mask, scan.Classes),
            StringComparer.Ordinal);

        // Include inherited protected/public base-class fields, constructor members, and properties.
        var visitedBases = new HashSet<string>(StringComparer.Ordinal) { decl.FullName };
        var baseQueue = new Queue<(CSharpScanner.ClassDecl Decl, int Depth)>();
        EnqueueBases(context, decl, 0, visitedBases, baseQueue);

        while (baseQueue.Count > 0)
        {
            var (baseDecl, depth) = baseQueue.Dequeue();
            if (context.FileByClass.TryGetValue(baseDecl, out var baseEntry))
            {
                var baseMembers = context.GetClassMemberMap(
                    baseDecl, baseEntry.File.Code, baseEntry.Mask, baseEntry.Scan.Classes);
                foreach (var (name, type) in baseMembers)
                    memberTypes.TryAdd(name, type);
            }

            if (depth + 1 < MaxBaseDepth)
                EnqueueBases(context, baseDecl, depth + 1, visitedBases, baseQueue);
        }

        var helperMethods = IndexHelperMethods(decl, file, scan, mask);
        return new ClassMethodIndex(memberTypes, helperMethods);
    }

    internal void CollectScopedBodyAndIdentifiers(
        string entryBodyCode,
        out HashSet<string> identifiers,
        out List<(string Body, int Start, int End)> visitedHelpers)
    {
        identifiers = new HashSet<string>(StringComparer.Ordinal);
        visitedHelpers = new List<(string Body, int Start, int End)>();

        ExtractIdentifiers(entryBodyCode, identifiers);
        if (HelperMethodsByName.Count == 0 || identifiers.Count == 0)
            return;

        var visitedNames = new HashSet<string>(StringComparer.Ordinal);
        var frontier = new Queue<(HashSet<string> Ids, int Depth)>();
        frontier.Enqueue((identifiers, 0));

        while (frontier.Count > 0)
        {
            var (currentIds, depth) = frontier.Dequeue();
            if (depth >= MaxHelperHops)
                continue;

            var nextIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in currentIds)
            {
                if (!HelperMethodsByName.TryGetValue(id, out var bodies) || !visitedNames.Add(id))
                    continue;

                foreach (var helper in bodies)
                {
                    visitedHelpers.Add(helper);
                    ExtractIdentifiers(helper.Body, nextIds);
                }
            }

            if (nextIds.Count > 0)
            {
                identifiers.UnionWith(nextIds);
                frontier.Enqueue((nextIds, depth + 1));
            }
        }
    }

    internal static void ExtractIdentifiers(string maskedCode, HashSet<string> target)
    {
        if (string.IsNullOrEmpty(maskedCode))
            return;

        var i = 0;
        var length = maskedCode.Length;
        while (i < length)
        {
            var c = maskedCode[i];
            if (char.IsLetter(c) || c == '_')
            {
                var start = i;
                i++;
                while (i < length && (char.IsLetterOrDigit(maskedCode[i]) || maskedCode[i] == '_'))
                    i++;
                target.Add(maskedCode[start..i]);
            }
            else
            {
                i++;
            }
        }
    }

    private static void EnqueueBases(
        SystemAnalysisContext context,
        CSharpScanner.ClassDecl decl,
        int depth,
        HashSet<string> visited,
        Queue<(CSharpScanner.ClassDecl Decl, int Depth)> queue)
    {
        foreach (var baseName in decl.BaseTypeNames)
        {
            var resolved = context.Hierarchy.TryResolveClass(
                baseName, decl.Namespace, decl.Usings, decl.ContainingTypes, 0, out _);
            if (resolved is not null && visited.Add(resolved.FullName))
                queue.Enqueue((resolved, depth));
        }
    }

    private static Dictionary<string, List<(string Body, int Start, int End)>> IndexHelperMethods(
        CSharpScanner.ClassDecl decl,
        SourceFile file,
        CSharpScanner.FileScan scan,
        SourceMask mask)
    {
        var map = new Dictionary<string, List<(string Body, int Start, int End)>>(StringComparer.Ordinal);
        if (decl.BodySpan.End <= decl.BodySpan.Start)
            return map;

        var bodyEnd = Math.Min(file.Code.Length, decl.BodySpan.End);
        MatchCollection matches;
        try
        {
            matches = MemberMethodRegex().Matches(file.Code, decl.BodySpan.Start);
        }
        catch (RegexMatchTimeoutException)
        {
            return map;
        }

        var count = 0;
        foreach (Match match in matches)
        {
            if (match.Index >= bodyEnd || count >= MaxHelperMethods)
                break;
            if (!mask.IsCode(match.Index))
                continue;
            if (!ReferenceEquals(DeepSliceSyntax.InnermostClass(scan.Classes, match.Index), decl))
                continue;

            var methodName = match.Groups["method"].Value;
            if (string.Equals(methodName, decl.Name, StringComparison.Ordinal))
                continue;

            var attrs = match.Groups["attrs"].Value;
            try
            {
                if (attrs.Length > 0 && EndpointMethodAttrRegex().IsMatch(attrs) && !NonActionAttrRegex().IsMatch(attrs))
                    continue;
            }
            catch (RegexMatchTimeoutException)
            {
                continue;
            }

            var openParen = match.Index + match.Length - 1;
            var closeParen = DeepSliceSyntax.FindMatchingParen(file.Code, mask, openParen);
            if (closeParen < 0 || closeParen >= bodyEnd)
                continue;

            var methodStart = closeParen + 1;
            var methodEnd = DeepSliceSyntax.FindMethodEnd(file.Code, mask, methodStart, bodyEnd);
            var helperBody = mask.CodeOnly(methodStart, Math.Max(0, methodEnd - methodStart));
            if (helperBody.Length == 0)
                continue;

            if (!map.TryGetValue(methodName, out var list))
                map[methodName] = list = new List<(string Body, int Start, int End)>();
            list.Add((helperBody, methodStart, methodEnd));
            count++;
        }

        return map;
    }
}
