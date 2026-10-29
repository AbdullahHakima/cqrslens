using System.Text.RegularExpressions;

namespace CqrsLens;

/// <summary>
/// Reads the public payload of a command/query, including immutable getter-only properties
/// that are not represented as ordinary property declarations.
/// </summary>
internal static partial class RequestPropertyPopulator
{
    [GeneratedRegex(@"(?:public|internal)\s+(?:required\s+)?(?!class\b|record\b|struct\b|interface\b)(?<type>(?:global::)?[A-Za-z_]\w*(?:\s*\.\s*[A-Za-z_]\w*)*(?:\s*<[^;{}()=]*>)?(?:\s*\[\s*\]\s*)*\??)\s+(?<name>[A-Za-z_]\w*)\s*\{\s*get\s*;\s*\}")]
    private static partial Regex GetterOnlyPropertyRegex();

    internal static void Populate(
        List<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles,
        AnalysisResult result)
    {
        var requestsByFull = result.Requests
            .GroupBy(r => r.FullName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        foreach (var (file, scan, mask) in parsedFiles)
        {
            foreach (var decl in scan.Classes)
            {
                if (!requestsByFull.TryGetValue(decl.FullName, out var candidates))
                    continue;

                var request = DeepSliceCandidateResolver.PickRequest(candidates, file.ProjectName, null);
                if (request is null) continue;

                foreach (var prop in decl.Properties)
                {
                    if (!prop.IsPublic || prop.IsStatic) continue;
                    if (request.Properties.Any(p => string.Equals(p.Name, prop.Name, StringComparison.Ordinal)))
                        continue;
                    request.Properties.Add(new RequestPropertyInfo(prop.Name, prop.Type));
                }

                // Also capture public/internal getter-only auto-properties on immutable Command/Query classes.
                if (decl.BodySpan.End > decl.BodySpan.Start)
                {
                    var bodyEnd = Math.Min(file.Code.Length, decl.BodySpan.End);
                    foreach (Match m in GetterOnlyPropertyRegex().Matches(file.Code, decl.BodySpan.Start))
                    {
                        if (m.Index >= bodyEnd) break;
                        if (!mask.IsCode(m.Index)) continue;
                        if (!ReferenceEquals(DeepSliceSyntax.InnermostClass(scan.Classes, m.Index), decl)) continue;

                        var propName = m.Groups["name"].Value;
                        var propType = Regex.Replace(m.Groups["type"].Value.Trim(), @"\s+", "");
                        if (request.Properties.Any(p => string.Equals(p.Name, propName, StringComparison.Ordinal)))
                            continue;
                        request.Properties.Add(new RequestPropertyInfo(propName, propType));
                    }
                }
            }
        }
    }
}
