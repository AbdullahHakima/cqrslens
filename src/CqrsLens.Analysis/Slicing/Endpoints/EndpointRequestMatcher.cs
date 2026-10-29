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
        IReadOnlyList<UsingDirective>? usings)
    {
        var results = new List<CqrsRequest>();

        void TryAdd(string typeRef)
        {
            var simple = CSharpScanner.UnqualifiedTypeName(typeRef);
            if (!requestsBySimple.TryGetValue(simple, out var hits))
                return;
            var picked = DeepSliceCandidateResolver.PickRequest(hits, projectName, usings);
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
}
