using System.Text.RegularExpressions;

namespace CqrsLens;

internal sealed record EfKeyConfiguration(string EntityFullName, IReadOnlyList<string> Properties);

internal sealed class EfFluentConfiguration
{
    public List<EfKeyConfiguration> Keys { get; } = new();
    public List<EfRelationshipConfiguration> Relationships { get; } = new();
}

/// <summary>
/// Extracts the small, common Fluent API surface that affects an ER diagram:
/// HasKey, HasOne/HasMany, WithOne/WithMany, HasForeignKey and IsRequired.
/// Unsupported builder calls are ignored; convention/annotation discovery still
/// applies, and the README documents the boundary.
/// </summary>
internal static class EfFluentConfigurationParser
{
    private static readonly Regex CallRegex = new(
        @"\.\s*(?<name>[A-Za-z_]\w*)\s*(?:<(?<generic>[^<>]*)>)?\s*\((?<args>[^()]*(?:\([^()]*\)[^()]*)*)\)",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex EntityRegex = new(
        @"\bEntity\s*<\s*(?<entity>[^<>]+?)\s*>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex MemberAccessRegex = new(
        @"\.\s*(?<name>[A-Za-z_]\w*)",
        RegexOptions.Singleline | RegexOptions.Compiled);

    public static EfFluentConfiguration ParseText(
        IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan)> files,
        TypeHierarchy hierarchy,
        AnalysisResult result,
        IReadOnlyDictionary<string, SourceMask>? masks = null)
    {
        var configuration = new EfFluentConfiguration();
        var seenRelationships = new HashSet<string>(StringComparer.Ordinal);
        var classesByFull = files
            .SelectMany(f => f.Scan.Classes)
            .GroupBy(c => c.FullName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var (file, scan) in files)
        {
            SourceMask? mask = null;
            foreach (var declaration in scan.Classes)
            {
                var isContext = CSharpScanner.IsDbContextBase(declaration.BaseList);
                var configTarget = ResolveConfigurationTarget(declaration, hierarchy);
                if (!isContext && configTarget is null) continue;

                mask ??= masks is not null && masks.TryGetValue(file.Path, out var sharedMask)
                    ? sharedMask
                    : SourceMask.Build(file.Code);
                var body = isContext
                    ? FindMethodBody(file.Code, mask, declaration.BodySpan, "OnModelCreating")
                    : FindMethodBody(file.Code, mask, declaration.BodySpan, "Configure");
                if (body is null) continue;

                foreach (var statement in SplitStatements(file.Code, mask, body.Value.Start, body.Value.End))
                    ParseStatement(
                        statement,
                        declaration,
                        configTarget,
                        hierarchy,
                        classesByFull,
                        file,
                        result,
                        configuration,
                        seenRelationships);
            }
        }

        return configuration;
    }

    private static void ParseStatement(
        (string Text, int Index) statement,
        CSharpScanner.ClassDecl declaration,
        string? defaultEntity,
        TypeHierarchy hierarchy,
        IReadOnlyDictionary<string, CSharpScanner.ClassDecl> classesByFull,
        SourceFile file,
        AnalysisResult result,
        EfFluentConfiguration configuration,
        HashSet<string> seenRelationships)
    {
        var calls = CallRegex.Matches(statement.Text).Cast<Match>().ToList();
        if (calls.Count == 0) return;

        var entity = defaultEntity;
        var entityMatch = EntityRegex.Match(statement.Text);
        if (entityMatch.Success)
        {
            entity = ResolveType(
                entityMatch.Groups["entity"].Value,
                declaration,
                hierarchy);
        }

        string? firstNavigation = null;
        string? inverseNavigation = null;
        string? foreignKey = null;
        string? firstGenericTarget = null;
        string? foreignKeyTarget = null;
        bool? firstIsCollection = null;
        bool? inverseIsCollection = null;
        bool? required = null;

        foreach (var call in calls)
        {
            var name = call.Groups["name"].Value;
            var generic = call.Groups["generic"].Success ? call.Groups["generic"].Value : null;
            var args = call.Groups["args"].Value;

            switch (name)
            {
                case "Entity" when generic is not null:
                    entity = ResolveType(generic, declaration, hierarchy);
                    firstNavigation = null;
                    inverseNavigation = null;
                    foreignKey = null;
                    firstIsCollection = null;
                    inverseIsCollection = null;
                    required = null;
                    break;

                case "HasKey" when entity is not null:
                    foreach (var property in ParseMembers(args))
                        configuration.Keys.Add(new EfKeyConfiguration(entity, new[] { property }));
                    break;

                case "HasOne":
                case "HasMany":
                    firstNavigation = ParseMember(args);
                    firstIsCollection = name == "HasMany";
                    firstGenericTarget = generic is null
                        ? null
                        : ResolveType(generic, declaration, hierarchy);
                    inverseNavigation = null;
                    inverseIsCollection = null;
                    foreignKey = null;
                    required = null;
                    break;

                case "WithOne":
                case "WithMany":
                    inverseNavigation = ParseMember(args);
                    inverseIsCollection = name == "WithMany";
                    break;

                case "HasForeignKey":
                    foreignKey = ParseMember(args);
                    foreignKeyTarget = generic is null
                        ? null
                        : ResolveType(generic, declaration, hierarchy);
                    break;

                case "IsRequired":
                    required = true;
                    break;
            }
        }

        if (entity is null || firstNavigation is null && firstIsCollection is null && firstGenericTarget is null)
            return;

        var entityDecl = FindClass(hierarchy, entity, declaration, classesByFull);
        var target = firstGenericTarget ??
                     (firstNavigation is null ? null : ResolveNavigationTarget(entityDecl, firstNavigation, hierarchy));
        if (target is null)
            return;

        var hasInverse = inverseIsCollection is not null || inverseNavigation is not null;
        var firstCollection = firstIsCollection == true;
        EntityRelationshipKind kind;
        string principal;
        string dependent;
        string? principalNavigation;
        string? dependentNavigation;

        if (hasInverse)
        {
            if (firstCollection && inverseIsCollection == true)
            {
                kind = EntityRelationshipKind.ManyToMany;
                principal = entity;
                dependent = target;
                principalNavigation = firstNavigation;
                dependentNavigation = inverseNavigation;
            }
            else if (firstCollection && inverseIsCollection == false)
            {
                kind = EntityRelationshipKind.OneToMany;
                principal = entity;
                dependent = target;
                principalNavigation = firstNavigation;
                dependentNavigation = inverseNavigation;
            }
            else if (!firstCollection && inverseIsCollection == true)
            {
                kind = EntityRelationshipKind.OneToMany;
                principal = target;
                dependent = entity;
                principalNavigation = inverseNavigation;
                dependentNavigation = firstNavigation;
            }
            else
            {
                kind = EntityRelationshipKind.OneToOne;
                var foreignKeyOnEntity = foreignKeyTarget == entity ||
                                         (foreignKey is not null && FindProperty(entityDecl, foreignKey, hierarchy) is not null);
                if (foreignKeyOnEntity)
                {
                    principal = target;
                    dependent = entity;
                    principalNavigation = inverseNavigation;
                    dependentNavigation = firstNavigation;
                }
                else
                {
                    principal = entity;
                    dependent = target;
                    principalNavigation = firstNavigation;
                    dependentNavigation = inverseNavigation;
                }
            }
        }
        else if (firstCollection)
        {
            kind = EntityRelationshipKind.OneToMany;
            principal = entity;
            dependent = target;
            principalNavigation = firstNavigation;
            dependentNavigation = null;
        }
        else
        {
            kind = EntityRelationshipKind.OneToMany;
            principal = target;
            dependent = entity;
            principalNavigation = null;
            dependentNavigation = firstNavigation;
        }

        var relationship = new EfRelationshipConfiguration(
            principal,
            dependent,
            principalNavigation,
            dependentNavigation,
            foreignKey,
            required,
            kind,
            "fluent");
        var key = string.Join("\u001f", principal, dependent,
            principalNavigation ?? "", dependentNavigation ?? "",
            foreignKey ?? "", kind.ToString());
        if (seenRelationships.Add(key))
            configuration.Relationships.Add(relationship);
    }

    private static string? ResolveConfigurationTarget(
        CSharpScanner.ClassDecl declaration,
        TypeHierarchy hierarchy)
    {
        if (!declaration.BaseList.Contains("IEntityTypeConfiguration", StringComparison.Ordinal))
            return null;

        foreach (var arguments in CSharpScanner.ExtractAllGenericArguments(
                     declaration.BaseList, "IEntityTypeConfiguration"))
        {
            if (arguments.Count == 0) continue;
            return ResolveType(arguments[0], declaration, hierarchy);
        }

        return null;
    }

    private static CSharpScanner.ClassDecl? FindClass(
        TypeHierarchy hierarchy,
        string fullName,
        CSharpScanner.ClassDecl context,
        IReadOnlyDictionary<string, CSharpScanner.ClassDecl> classesByFull)
    {
        if (classesByFull.TryGetValue(fullName, out var direct))
            return direct;

        var simple = CSharpScanner.SimpleName(fullName);
        return hierarchy.TryResolveClass(
            simple,
            context.Namespace,
            context.Usings,
            context.ContainingTypes,
            0,
            out _);
    }

    private static string? ResolveNavigationTarget(
        CSharpScanner.ClassDecl? owner,
        string navigation,
        TypeHierarchy hierarchy)
    {
        if (owner is null) return null;
        var property = FindProperty(owner, navigation, hierarchy);
        if (property is null) return null;
        var type = property.Value.Property.Type;
        if (EfAnnotations.TryGetCollectionElementType(type, out var element))
            type = element;
        return ResolveType(type, property.Value.Declaring, hierarchy);
    }

    private static (CSharpScanner.PropertyDecl Property, CSharpScanner.ClassDecl Declaring)? FindProperty(
        CSharpScanner.ClassDecl? owner,
        string name,
        TypeHierarchy? hierarchy = null)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var current = owner;
        while (current is not null && seen.Add(current.FullName))
        {
            var match = current.Properties.FirstOrDefault(p =>
                string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return (match, current);

            if (hierarchy is null) break;
            current = FirstBaseClass(current, hierarchy);
        }

        return null;
    }

    private static CSharpScanner.ClassDecl? FirstBaseClass(
        CSharpScanner.ClassDecl declaration,
        TypeHierarchy hierarchy)
    {
        foreach (var part in CSharpScanner.SplitTopLevel(declaration.BaseList))
        {
            var (name, args) = CSharpScanner.ParseTypeReference(part);
            if (name.Length == 0) continue;
            var resolved = hierarchy.TryResolveClass(
                name,
                declaration.Namespace,
                declaration.Usings,
                declaration.ContainingTypes,
                args.Count,
                out _);
            if (resolved is not null)
                return resolved;
        }

        return null;
    }

    private static string? ResolveType(
        string writtenType,
        CSharpScanner.ClassDecl context,
        TypeHierarchy hierarchy)
    {
        var normalized = EfAnnotations.NormalizeType(writtenType);
        if (normalized.EndsWith('?')) normalized = normalized[..^1];
        var (name, args) = CSharpScanner.ParseTypeReference(normalized);
        if (name.Length == 0) return null;
        var resolved = hierarchy.TryResolveClass(
            name,
            context.Namespace,
            context.Usings,
            context.ContainingTypes,
            args.Count,
            out _);
        return resolved?.FullName;
    }

    private static string? ParseMember(string args)
    {
        var members = ParseMembers(args);
        return members.Count > 0 ? members[0] : null;
    }

    private static IReadOnlyList<string> ParseMembers(string args)
    {
        var text = args.Trim();
        if (text.Length == 0) return Array.Empty<string>();

        var arrow = text.IndexOf("=>", StringComparison.Ordinal);
        if (arrow >= 0)
            text = text[(arrow + 2)..].Trim();

        foreach (Match quoted in Regex.Matches(text, @"""([^""]+)"""))
        {
            if (quoted.Groups[1].Value.Length > 0)
                return new[] { quoted.Groups[1].Value };
        }

        var members = MemberAccessRegex.Matches(text)
            .Cast<Match>()
            .Select(m => m.Groups["name"].Value)
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (members.Count > 0) return members;

        var bare = Regex.Match(text, @"^@?([A-Za-z_]\w*)$");
        return bare.Success ? new[] { bare.Groups[1].Value } : Array.Empty<string>();
    }

    private static (int Start, int End)? FindMethodBody(
        string code,
        SourceMask mask,
        (int Start, int End) classSpan,
        string methodName)
    {
        var regex = new Regex(@"\b" + Regex.Escape(methodName) + @"\s*\(", RegexOptions.Compiled);
        foreach (Match match in regex.Matches(code, classSpan.Start))
        {
            if (match.Index >= classSpan.End) break;
            if (!mask.IsCode(match.Index)) continue;

            var openParen = code.IndexOf('(', match.Index);
            if (openParen < 0) continue;
            var closeParen = FindMatching(code, mask, openParen, '(', ')');
            if (closeParen < 0) continue;
            var openBrace = code.IndexOf('{', closeParen + 1);
            if (openBrace < 0 || openBrace >= classSpan.End) continue;
            var closeBrace = mask.FindMatchingBrace(openBrace);
            if (closeBrace < 0) continue;
            return (openBrace + 1, closeBrace);
        }

        return null;
    }

    private static int FindMatching(
        string code,
        SourceMask mask,
        int start,
        char open,
        char close)
    {
        var depth = 0;
        for (var i = start; i < code.Length; i++)
        {
            if (!mask.IsCode(i)) continue;
            if (code[i] == open) depth++;
            else if (code[i] == close)
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }

    private static IEnumerable<(string Text, int Index)> SplitStatements(
        string code,
        SourceMask mask,
        int start,
        int end)
    {
        var parens = 0;
        var braces = 0;
        var statementStart = start;
        for (var i = start; i < end && i < code.Length; i++)
        {
            if (!mask.IsCode(i)) continue;
            switch (code[i])
            {
                case '(':
                    parens++;
                    break;
                case ')':
                    if (parens > 0) parens--;
                    break;
                case '{':
                    braces++;
                    break;
                case '}':
                    if (braces > 0) braces--;
                    break;
                case ';' when parens == 0 && braces == 0:
                    yield return (code[statementStart..i], statementStart);
                    statementStart = i + 1;
                    break;
            }
        }

        if (statementStart < end)
            yield return (code[statementStart..end], statementStart);
    }
}
