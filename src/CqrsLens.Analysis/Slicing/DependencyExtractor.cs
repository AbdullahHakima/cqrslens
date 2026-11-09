using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace CqrsLens;

/// <summary>
/// Extracts constructor and primary-constructor dependencies from a handler/service class.
/// Only dependency type names are captured; the caller decides whether they represent
/// repositories, DbContexts or ordinary services.
/// </summary>
internal static class DependencyExtractor
{
    /// <summary>Constructor signatures are per-class regexes because the class name is part of the
    /// pattern. Cache them so deep-slice scans do not recompile one regex per class declaration.</summary>
    private static readonly ConcurrentDictionary<string, Regex> ConstructorRegexCache = new(StringComparer.Ordinal);

    internal static void Extract(
        CSharpScanner.ClassDecl decl,
        string code,
        SourceMask mask,
        List<CSharpScanner.ClassDecl> classes,
        List<string> dependencies)
    {
        if (!string.IsNullOrWhiteSpace(decl.PrimaryConstructorParameters))
            AddParameterTypes(decl.PrimaryConstructorParameters, dependencies);

        ExtractConstructorParameters(decl, code, mask, classes, (type, _) =>
        {
            if (!dependencies.Contains(type, StringComparer.Ordinal))
                dependencies.Add(type);
        });
    }

    /// <summary>
    /// Collects the (type, name) pair of every constructor parameter — the primary
    /// constructor when the declaration has one, plus every explicit constructor. This is
    /// the same parameter capture as <see cref="Extract"/>, retaining the parameter name so
    /// a caller can map an injected member to its declared type.
    /// </summary>
    internal static void ExtractParameters(
        CSharpScanner.ClassDecl decl,
        string code,
        SourceMask mask,
        List<CSharpScanner.ClassDecl> classes,
        List<(string Type, string Name)> target)
    {
        if (!string.IsNullOrWhiteSpace(decl.PrimaryConstructorParameters))
            AddParameterMembers(decl.PrimaryConstructorParameters, target);

        ExtractConstructorParameters(decl, code, mask, classes, (type, name) =>
        {
            if (name is null) return;
            if (target.Contains((type, name))) return;
            target.Add((type, name));
        });
    }

    private static void ExtractConstructorParameters(
        CSharpScanner.ClassDecl decl,
        string code,
        SourceMask mask,
        List<CSharpScanner.ClassDecl> classes,
        Action<string, string?> onParameter)
    {
        if (decl.BodySpan.End <= decl.BodySpan.Start)
            return;

        // A constructor declaration must contain its class name. This cheap span
        // check avoids compiling a per-class regex for every entity/DTO/endpoint.
        var bodyLength = Math.Min(code.Length, decl.BodySpan.End) - decl.BodySpan.Start;
        if (bodyLength <= 0 ||
            code.IndexOf(decl.Name, decl.BodySpan.Start, bodyLength, StringComparison.Ordinal) < 0)
            return;

        var ctorPattern = ConstructorRegexCache.GetOrAdd(
            decl.Name,
            static name => new Regex(
                $@"\b(?:public|internal|protected|private)\s+{Regex.Escape(name)}\s*\((?<params>[^\)]*)\)",
                RegexOptions.Singleline));

        var bodyEnd = Math.Min(code.Length, decl.BodySpan.End);
        foreach (Match m in ctorPattern.Matches(code, decl.BodySpan.Start))
        {
            if (m.Index >= bodyEnd) break;
            if (!mask.IsCode(m.Index)) continue;
            if (!ReferenceEquals(DeepSliceSyntax.InnermostClass(classes, m.Index), decl)) continue;

            foreach (var raw in CSharpScanner.SplitTopLevel(m.Groups["params"].Value))
            {
                var paramType = DeepSliceSyntax.ExtractParameterType(raw);
                if (string.IsNullOrEmpty(paramType)) continue;
                onParameter(paramType, DeepSliceSyntax.ExtractParameterName(raw));
            }
        }
    }

    private static void AddParameterTypes(string rawParameters, List<string> target)
    {
        foreach (var raw in CSharpScanner.SplitTopLevel(rawParameters))
        {
            var paramType = DeepSliceSyntax.ExtractParameterType(raw);
            if (string.IsNullOrEmpty(paramType)) continue;
            if (!target.Contains(paramType, StringComparer.Ordinal))
                target.Add(paramType);
        }
    }

    private static void AddParameterMembers(string rawParameters, List<(string Type, string Name)> target)
    {
        foreach (var raw in CSharpScanner.SplitTopLevel(rawParameters))
        {
            var paramType = DeepSliceSyntax.ExtractParameterType(raw);
            var paramName = DeepSliceSyntax.ExtractParameterName(raw);
            if (string.IsNullOrEmpty(paramType) || string.IsNullOrEmpty(paramName)) continue;
            if (!target.Contains((paramType, paramName)))
                target.Add((paramType, paramName));
        }
    }
}
