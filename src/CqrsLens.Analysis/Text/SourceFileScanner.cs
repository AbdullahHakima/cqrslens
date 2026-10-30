using System.Text.RegularExpressions;

namespace CqrsLens;

/// <summary>
/// Text-only scanner that turns one C# file into a self-contained <see cref="CSharpScanner.FileScan"/>.
/// It detects declarations, base lists and property shapes without compiling or resolving symbols;
/// later stages merge and interpret the fragments.
/// </summary>
internal static partial class SourceFileScanner
{
    // Matches: class|record|struct Name<...> (primary-ctor) [: base list] { or ;
    // The primary-constructor parameter list may not run past a ") :" sequence:
    // that colon opens the base list (DbContext(options) is the first base entry
    // of a primary-constructor class, not part of its parameter list).
    [GeneratedRegex(@"(?:class|record(?:\s+class|\s+struct)?|struct)\s+(?<name>\w+)(?:<(?<typeparams>[^>]*)>)?\s*(?:\((?<params>(?:(?!\)\s*:)[^{};])*)\))?\s*(?:(?!where\b)\s*:\s*(?<base>[^{;]+?))?\s*(?:\s*where\s+\w+\s*:[^{;]+)?[{;]",
        RegexOptions.Singleline)]
    private static partial Regex DeclarationRegex();

    // Best-effort property match. The accessor block keeps this from matching
    // methods and fields; attributes/modifiers are kept for EF annotations.
    // The leading (?<![A-Za-z0-9_]) is a performance guard, not a semantic one:
    // the <type> group starts with [A-Za-z_]\w*, so without it the engine restarts
    // this whole pattern at every offset of a long \w run (e.g. a multi-megabyte
    // seed-data string literal) and each attempt rescans to the end of the run,
    // making the scan O(run^2). A C# type never starts immediately after an
    // identifier character, so requiring a non-word boundary drops only start
    // positions that could not produce a match. Matches on corpus files are
    // byte-identical with and without it.
    [GeneratedRegex(@"(?<![A-Za-z0-9_])(?<attrs>(?:\[\s*[^\[\]]*\]\s*)*)(?<mods>(?:(?:public|private|protected|internal|static|virtual|override|sealed|required|new|readonly|abstract|unsafe|partial|extern)\s+)*)(?!\b(?:class|record|struct|interface|enum|namespace|return)\b)(?<type>(?:global::)?[A-Za-z_]\w*(?:\s*\.\s*[A-Za-z_]\w*)*(?:\s*<[^;{}()=]*>)?(?:\s*\[\s*\]\s*)*\??)\s+(?<name>[A-Za-z_]\w*)\s*(?<rest>\{\s*(?:(?:public|private|protected|internal)\s+)?(?:get|set|init)\b[^{}]*\})", RegexOptions.Singleline)]
    private static partial Regex PropertyRegex();

    // Matches: interface Name<...> [: base list] { or ;
    [GeneratedRegex(@"\binterface\s+(\w+)(?:<([^>]*)>)?\s*(?:(?!where\b)\s*:\s*([^{;]+?))?\s*(?:\s*where\s+\w+\s*:[^{;]+)?[{;]",
        RegexOptions.Singleline)]
    private static partial Regex InterfaceDeclarationRegex();

    /// <summary>Extracts the first namespace declared in a source file ("" when none).</summary>
    internal static string ExtractNamespace(string code) => NamespaceMap.Build(code).FirstNamespace;

    /// <summary>Scans one file's text into a fragment (pure; safe to run in parallel).</summary>
    internal static CSharpScanner.FileScan ScanText(string code) => ScanText(code, SourceMask.Build(code));

    internal static CSharpScanner.FileScan ScanText(string code, SourceMask mask)
    {
        var namespaces = NamespaceMap.Build(code, mask);
        var scan = new CSharpScanner.FileScan { Namespace = namespaces.FirstNamespace };
        scan.Usings.AddRange(UsingDirectiveParser.Extract(code, mask, namespaces));

        foreach (Match m in InterfaceDeclarationRegex().Matches(code))
        {
            if (!mask.IsCode(m.Index)) continue;
            var name = m.Groups[1].Value;
            var baseList = m.Groups[3].Value;
            var openBrace = m.Value.EndsWith('{') ? m.Index + m.Length - 1 : -1;
            scan.InterfaceDecls.Add(new CSharpScanner.InterfaceDecl
            {
                Name = name,
                Namespace = namespaces.NamespaceAt(m.Index),
                BaseList = baseList,
                TypeParameters = CSharpSyntax.ParseTypeParameters(m.Groups[2].Value),
                Usings = scan.Usings,
                BodySpan = ComputeTypeSpan(code, mask, m.Index, openBrace),
            });
            if (!scan.Interfaces.ContainsKey(name))
                scan.Interfaces[name] = new List<string>();
            scan.Interfaces[name].AddRange(CSharpSyntax.ExtractBaseTypeNames(baseList));
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
            scan.Classes.Add(new CSharpScanner.ClassDecl
            {
                Name = typeName,
                Namespace = namespaces.NamespaceAt(m.Index),
                BaseList = baseList,
                BaseTypeNames = CSharpSyntax.ExtractBaseTypeNames(baseList),
                BaseRawNames = CSharpSyntax.ExtractBaseRawNames(baseList),
                TypeParameters = CSharpSyntax.ParseTypeParameters(m.Groups["typeparams"].Value),
                Usings = scan.Usings,
                IsAbstract = CSharpSyntax.IsAbstractDeclaration(code, m.Index),
                BodySpan = span,
                DeclarationIndex = m.Index,
                NameIndex = m.Groups["name"].Index,
                PrimaryConstructorParameters = m.Groups["params"].Success
                    ? m.Groups["params"].Value
                    : null,
            });
        }

        PopulateContainingTypes(scan);
        PopulateProperties(scan, code, mask);
        return scan;
    }

    private static void PopulateContainingTypes(CSharpScanner.FileScan scan)
    {
        var containers = scan.Classes
            .Select(c => (c.Name, c.BodySpan, c.ContainingTypes))
            .Concat(scan.InterfaceDecls
                .Select(i => (i.Name, i.BodySpan, i.ContainingTypes)))
            .OrderBy(c => c.BodySpan.Start)
            .ToList();

        foreach (var container in containers)
        {
            var parent = containers
                .Where(p => p.BodySpan.Start < container.BodySpan.Start &&
                            container.BodySpan.Start < p.BodySpan.End)
                .OrderByDescending(p => p.BodySpan.Start)
                .FirstOrDefault();
            if (parent.Name is null) continue;
            container.ContainingTypes.AddRange(parent.ContainingTypes);
            container.ContainingTypes.Add(parent.Name);
        }
    }

    private static void PopulateProperties(CSharpScanner.FileScan scan, string code, SourceMask mask)
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

                var property = new CSharpScanner.PropertyDecl
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

    private static CSharpScanner.ClassDecl? InnermostClass(
        IReadOnlyList<CSharpScanner.ClassDecl> classes,
        int index)
    {
        CSharpScanner.ClassDecl? owner = null;
        foreach (var candidate in classes)
        {
            if (index < candidate.BodySpan.Start || index >= candidate.BodySpan.End)
                continue;
            if (owner is null || candidate.BodySpan.Start > owner.BodySpan.Start)
                owner = candidate;
        }
        return owner;
    }

    private static void AddPrimaryConstructorProperties(CSharpScanner.ClassDecl decl)
    {
        if (string.IsNullOrWhiteSpace(decl.PrimaryConstructorParameters)) return;

        foreach (var rawParameter in CSharpSyntax.SplitTopLevel(decl.PrimaryConstructorParameters))
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

            var property = new CSharpScanner.PropertyDecl
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
}
