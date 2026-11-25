using System.Text.RegularExpressions;
using static CqrsLens.DeepSlicePatterns;

namespace CqrsLens;

/// <summary>
/// Finds CQRS requests referenced by an endpoint's parameters or body, and indexes
/// FastEndpoints route constants so const-based routes can be resolved.
/// </summary>
internal static class EndpointRequestMatcher
{
    public static Dictionary<string, string> BuildConstStringLookup(
        List<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (file, scan, mask) in parsedFiles)
        {
            // FastEndpoints route constants are rare; avoid a regex pass over every
            // source file in large solutions. Every candidate contains `const`.
            if (!file.Code.Contains("const", StringComparison.Ordinal)) continue;

            foreach (var declaration in scan.Classes)
            {
                if (declaration.BodySpan.End <= declaration.BodySpan.Start) continue;
                var bodyEnd = Math.Min(file.Code.Length, declaration.BodySpan.End);
                foreach (Match match in ApiEndpointPatterns.ConstString().Matches(
                             file.Code, declaration.BodySpan.Start))
                {
                    if (match.Index >= bodyEnd) break;
                    if (!mask.IsCode(match.Index)) continue;
                    var name = match.Groups["name"].Value;
                    var value = match.Groups["value"].Value;
                    map.TryAdd($"{declaration.Name}.{name}", value);
                    map.TryAdd($"{declaration.FullName}.{name}", value);
                }
            }
        }
        return map;
    }

    public static List<CqrsRequest> FindReferencedRequests(
        string parametersSection,
        string bodySection,
        Dictionary<string, List<CqrsRequest>> requestsBySimple,
        string? projectName,
        IReadOnlyList<UsingDirective>? usings,
        string? callerNamespace = null,
        IReadOnlyList<string>? containingTypes = null)
    {
        var results = new List<CqrsRequest>();

        void TryAdd(string typeRef)
        {
            var picked = ResolveRequest(
                typeRef, requestsBySimple, projectName, usings, callerNamespace, containingTypes);
            if (picked is not null && !results.Contains(picked))
                results.Add(picked);
        }

        // 1. Check parameter types
        if (!string.IsNullOrWhiteSpace(parametersSection))
        {
            foreach (var rawParameter in CSharpScanner.SplitTopLevel(parametersSection))
            {
                var parameterType = DeepSliceSyntax.ExtractParameterType(rawParameter);
                if (!string.IsNullOrEmpty(parameterType))
                    TryAdd(parameterType);
            }
        }

        // 2. Check parameter patterns and object creations `new RequestType(` or `new RequestType {`
        foreach (Match match in ParameterTypeRegex().Matches(bodySection))
            TryAdd(match.Groups["type"].Value);
        foreach (Match match in ObjectCreationRegex().Matches(bodySection))
            TryAdd(match.Groups["type"].Value);

        // 3. Check generic Send<RequestType> / InvokeAsync<RequestType>
        foreach (Match match in GenericDispatchRegex().Matches(bodySection))
            TryAdd(match.Groups["type"].Value);

        return results;
    }

    internal static CqrsRequest? ResolveRequest(
        string typeRef,
        Dictionary<string, List<CqrsRequest>> requestsBySimple,
        string? projectName,
        IReadOnlyList<UsingDirective>? usings,
        string? callerNamespace,
        IReadOnlyList<string>? containingTypes = null)
    {
        var type = typeRef.Any(char.IsWhiteSpace) ? string.Concat(typeRef.Where(c => !char.IsWhiteSpace(c))) : typeRef;
        var simple = CSharpScanner.UnqualifiedTypeName(type);
        var qualifier = CSharpSyntax.NamespaceOfType(type);
        var fullName = qualifier.Length == 0 ? simple : $"{qualifier}.{simple}";
        if (type.StartsWith("global::", StringComparison.Ordinal))
            return UniqueMatch(requestsBySimple.GetValueOrDefault(simple), fullName);

        var separator = fullName.IndexOf('.');
        var firstSegment = separator < 0 ? fullName : fullName[..separator];
        var aliases = usings?.Where(directive => directive.Alias == firstSegment &&
            IsInScope(callerNamespace, directive.ContainerNamespace)).ToArray();
        if (aliases is { Length: > 0 })
        {
            var suffix = separator < 0 ? "" : fullName[separator..];
            var matches = aliases.Select(directive => CSharpSyntax.SimpleName(directive.Target + suffix))
                .SelectMany(name => requestsBySimple.GetValueOrDefault(name) ?? [])
                .Where(candidate => aliases.Any(directive => candidate.FullName == directive.Target + suffix));
            return UniqueMatch(matches.Distinct(), null);
        }

        if (!requestsBySimple.TryGetValue(simple, out var candidates))
            return null;

        if (containingTypes is { Count: > 0 })
        {
            for (var depth = containingTypes.Count; depth >= 1; depth--)
            {
                var scopePrefix = string.Join(".", containingTypes.Take(depth));
                var prefix = string.IsNullOrEmpty(callerNamespace)
                    ? scopePrefix
                    : $"{callerNamespace}.{scopePrefix}";
                var candidateFull = $"{prefix}.{fullName}";
                if (candidates.Any(candidate => candidate.FullName == candidateFull))
                    return UniqueMatch(candidates, candidateFull);
            }
        }

        if (qualifier.Length == 0)
        {
            if (!string.IsNullOrEmpty(callerNamespace))
            {
                for (var scope = callerNamespace; !string.IsNullOrEmpty(scope); scope = CSharpSyntax.NamespaceOf(scope))
                {
                    var scopedSimple = $"{scope}.{simple}";
                    if (candidates.Any(candidate => candidate.FullName == scopedSimple))
                        return UniqueMatch(candidates, scopedSimple);
                }
            }

            return DeepSliceCandidateResolver.PickRequest(candidates, projectName, usings);
        }

        // A qualified reference can be relative to an enclosing or imported
        // namespace. If it cannot be resolved, its final segment is insufficient
        // evidence to link it to a different request from another namespace.
        for (var scope = callerNamespace; !string.IsNullOrEmpty(scope); scope = CSharpSyntax.NamespaceOf(scope))
        {
            var scopedName = $"{scope}.{fullName}";
            if (candidates.Any(candidate => candidate.FullName == scopedName))
                return UniqueMatch(candidates, scopedName);
        }

        if (candidates.Any(candidate => candidate.FullName == fullName))
            return UniqueMatch(candidates, fullName);

        var importedNames = usings?.Where(directive => IsInScope(callerNamespace, directive.ContainerNamespace))
            .SelectMany(directive => directive.NamespaceCandidates())
            .Select(import => $"{import}.{fullName}").ToHashSet(StringComparer.Ordinal);
        return importedNames is null
            ? null
            : UniqueMatch(candidates.Where(candidate => importedNames.Contains(candidate.FullName)), null);
    }

    private static CqrsRequest? UniqueMatch(IEnumerable<CqrsRequest>? candidates, string? fullName)
    {
        var matching = candidates?.Where(candidate => fullName is null || candidate.FullName == fullName).Take(2).ToArray();
        return matching is { Length: 1 } ? matching[0] : null;
    }

    private static bool IsInScope(string? callerNamespace, string? directiveNamespace)
        => string.IsNullOrEmpty(directiveNamespace) || callerNamespace == directiveNamespace ||
           callerNamespace?.StartsWith(directiveNamespace + ".", StringComparison.Ordinal) == true;
}
