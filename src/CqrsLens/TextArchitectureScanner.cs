using System.Collections.Concurrent;

namespace CqrsLens;

internal sealed class TextArchitectureScanner : IArchitectureScanner
{
    public void Scan(
        IReadOnlyList<SourceFile> files,
        IReadOnlySet<string> markers,
        AnalysisResult result)
    {
        var fragments = new ConcurrentBag<(SourceFile File, CSharpScanner.FileScan Scan)>();
        Parallel.ForEach(files, file =>
        {
            try
            {
                fragments.Add((file, CSharpScanner.ScanText(file.Code)));
            }
            catch
            {
                // Keep text mode best-effort: one source file should not abort the scan.
            }
        });

        var ordered = fragments
            .OrderBy(f => f.File.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rawInterfaceMap = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (_, scan) in ordered)
        {
            foreach (var (name, bases) in scan.Interfaces)
            {
                var fullName = string.IsNullOrEmpty(scan.Namespace) ? name : $"{scan.Namespace}.{name}";
                if (!rawInterfaceMap.TryGetValue(fullName, out var list))
                    rawInterfaceMap[fullName] = list = new List<string>();
                foreach (var b in bases)
                    if (!list.Contains(b)) list.Add(b);
            }
        }

        var interfacesBySimpleName = rawInterfaceMap.Keys
            .GroupBy(CSharpScanner.SimpleName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var interfaceMap = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (name, bases) in rawInterfaceMap)
        {
            var ns = CSharpScanner.NamespaceOf(name);
            interfaceMap[name] = bases
                .Select(b => ResolveInterfaceName(b, ns, rawInterfaceMap, interfacesBySimpleName))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        foreach (var (file, scan) in ordered)
        {
            CSharpScanner.ApplyScan(scan, file.Code, file.ProjectName, result);
            DiscoverMarkerRequests(
                scan,
                file.ProjectName,
                interfaceMap,
                interfacesBySimpleName,
                markers,
                result);
        }
    }

    private static void DiscoverMarkerRequests(
        CSharpScanner.FileScan scan,
        string? projectName,
        Dictionary<string, List<string>> interfaceMap,
        Dictionary<string, List<string>> interfacesBySimpleName,
        IReadOnlySet<string> markers,
        AnalysisResult result)
    {
        foreach (var decl in scan.Classes)
        {
            if (decl.BaseRawNames.Contains("IRequest")) continue;
            if (decl.BaseRawNames.Contains("IRequestHandler")) continue;
            if (decl.BaseRawNames.Count == 0) continue;

            if (decl.BaseRawNames.Any(b => b is "DbContext" or "IdentityDbContext" or
                                                "AbstractValidator" or "IValidator" or
                                                "IPipelineBehavior"))
                continue;

            var directInterfaces = decl.BaseTypeNames
                .Select(name => ResolveInterfaceName(name, scan.Namespace, interfaceMap, interfacesBySimpleName));
            var reached = MarkerResolver.ReachableMarkers(directInterfaces, interfaceMap, markers);
            if (reached.Count == 0) continue;

            if (!reached.Contains("IRequest") && !reached.Any(markers.Contains))
                continue;

            var via = reached.FirstOrDefault(m => m != "IRequest")
                      ?? decl.BaseRawNames.FirstOrDefault(b => b != "IRequest");

            if (result.Requests.Any(r => r.Name == decl.Name && r.Namespace == scan.Namespace))
                continue;

            var request = new CqrsRequest
            {
                Name = decl.Name,
                Namespace = scan.Namespace,
                ProjectName = projectName,
                ViaMarker = via,
            };
            request.ResponseType =
                MarkerResolver.TryExtractMarkerResponse(decl, reached)
                ?? decl.BaseRawNames
                    .Select(b => CSharpScanner.ExtractGenericArguments(decl.BaseList, b)?.FirstOrDefault())
                    .FirstOrDefault(a => a is not null);
            if (!request.Name.EndsWith("Command", StringComparison.Ordinal) &&
                !request.Name.EndsWith("Query", StringComparison.Ordinal))
                request.Kind = MarkerResolver.ClassifyMarkerRequest(
                    request.Name, decl.BaseRawNames.Concat(reached));
            result.Requests.Add(request);
        }
    }

    private static string ResolveInterfaceName(
        string name,
        string currentNamespace,
        IReadOnlyDictionary<string, List<string>> interfaceMap,
        IReadOnlyDictionary<string, List<string>> interfacesBySimpleName)
    {
        if (name.Contains('.')) return name;

        var localName = string.IsNullOrEmpty(currentNamespace) ? name : $"{currentNamespace}.{name}";
        if (interfaceMap.ContainsKey(localName))
            return localName;

        return interfacesBySimpleName.TryGetValue(name, out var candidates) && candidates.Count == 1
            ? candidates[0]
            : name;
    }
}
