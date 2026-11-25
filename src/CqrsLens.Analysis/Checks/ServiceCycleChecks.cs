namespace CqrsLens;

/// <summary>
/// Detects unambiguous circular dependencies between scanned production services/classes
/// from <see cref="AnalysisResult.ServiceCallEdges"/> and DI
/// <see cref="AnalysisResult.ServiceResolutions"/>, excluding test projects and self-edges.
/// </summary>
internal static class ServiceCycleChecks
{
    private const int MaxCyclesPerScc = 20;
    private const int MaxCycleLength = 25;

    internal static void Run(AnalysisResult result, List<CheckFinding> findings)
    {
        if (result.ServiceCallEdges.Count == 0)
            return;

        var testProjects = CollectTestProjects(result);
        var dbContexts = result.DbContexts
            .Select(c => c.FullName)
            .ToHashSet(StringComparer.Ordinal);

        var resolutionsBySignature = new Dictionary<string, ServiceResolution>(StringComparer.Ordinal);
        var uniqueByInterfaceName = new Dictionary<string, ServiceResolution?>(StringComparer.Ordinal);
        var ambiguousInterfaces = new HashSet<string>(StringComparer.Ordinal);
        var typeMetadata = new Dictionary<string, (string? ProjectName, SourceLocation? Location)>(StringComparer.Ordinal);

        foreach (var sr in result.ServiceResolutions)
        {
            resolutionsBySignature.TryAdd(sr.Interface, sr);
            if (!uniqueByInterfaceName.TryGetValue(sr.InterfaceName, out var existing))
                uniqueByInterfaceName[sr.InterfaceName] = sr;
            else if (existing is not null)
                uniqueByInterfaceName[sr.InterfaceName] = null;

            if (sr.IsAmbiguous || string.IsNullOrWhiteSpace(sr.Implementation))
            {
                ambiguousInterfaces.Add(sr.Interface);
                ambiguousInterfaces.Add(sr.InterfaceName);
            }

            foreach (var candidate in sr.Candidates)
            {
                if (!candidate.DeclaredInScan || candidate.Location is null)
                    continue;

                var proj = InferProjectName(result, candidate.Location);
                if (!typeMetadata.ContainsKey(candidate.Type))
                    typeMetadata[candidate.Type] = (proj, candidate.Location);
            }
        }

        foreach (var handler in result.Handlers)
            typeMetadata.TryAdd(handler.FullName, (handler.ProjectName, handler.Location));
        foreach (var nh in result.NotificationHandlers)
            typeMetadata.TryAdd(nh.FullName, (nh.ProjectName, nh.Location));

        var adjacency = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var edgeInfo = new Dictionary<(string From, string To), (string? ProjectName, SourceLocation? Location)>();

        foreach (var edge in result.ServiceCallEdges
            .OrderBy(e => e.From, StringComparer.Ordinal)
            .ThenBy(e => e.To, StringComparer.Ordinal)
            .ThenBy(e => e.Via, StringComparer.Ordinal)
            .ThenBy(e => e.Method, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(edge.From) || string.IsNullOrWhiteSpace(edge.To))
                continue;

            var fromProject = edge.ProjectName
                ?? InferProjectName(result, edge.Location)
                ?? (typeMetadata.TryGetValue(edge.From, out var fromMeta) ? fromMeta.ProjectName : null);

            if (!string.IsNullOrWhiteSpace(fromProject) && testProjects.Contains(fromProject))
                continue;

            if (IsAmbiguousInterface(edge.Via, resolutionsBySignature, uniqueByInterfaceName, ambiguousInterfaces))
                continue;

            if (!TryResolveTargetType(edge.To, resolutionsBySignature, uniqueByInterfaceName, out var target))
                continue;

            if (string.Equals(edge.From, target, StringComparison.Ordinal))
                continue;

            if (dbContexts.Contains(edge.From) || dbContexts.Contains(target))
                continue;

            if (typeMetadata.TryGetValue(target, out var targetMeta) &&
                !string.IsNullOrWhiteSpace(targetMeta.ProjectName) &&
                testProjects.Contains(targetMeta.ProjectName))
            {
                continue;
            }

            if (!adjacency.TryGetValue(edge.From, out var neighbors))
                adjacency[edge.From] = neighbors = new List<string>();

            if (!neighbors.Contains(target, StringComparer.Ordinal))
            {
                neighbors.Add(target);
                edgeInfo[(edge.From, target)] = (fromProject, edge.Location);
            }

            adjacency.TryAdd(target, new List<string>());
            if (!typeMetadata.ContainsKey(edge.From))
                typeMetadata[edge.From] = (fromProject, edge.Location);
        }

        if (adjacency.Count < 2)
            return;

        foreach (var list in adjacency.Values)
            list.Sort(StringComparer.Ordinal);

        var vertices = adjacency.Keys
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();

        var sccs = FindStronglyConnectedComponents(vertices, adjacency);
        var reportedCycles = new HashSet<string>(StringComparer.Ordinal);

        foreach (var scc in sccs)
        {
            if (scc.Count < 2)
                continue;

            scc.Sort(StringComparer.Ordinal);
            EnumerateElementaryCycles(scc, adjacency, edgeInfo, typeMetadata, reportedCycles, findings);
        }
    }

    private static HashSet<string> CollectTestProjects(AnalysisResult result)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in result.Projects)
        {
            if (string.Equals(p.Layer, "Tests", StringComparison.OrdinalIgnoreCase))
                names.Add(p.Name);
        }

        foreach (var tp in TestProjectDiscovery.DiscoverTestProjects(result))
            names.Add(tp.Name);

        foreach (var sp in result.ServiceProfiles)
        {
            if (sp.RuntimeRole == ServiceRuntimeRole.Tests)
                names.Add(sp.ProjectName);
        }

        return names;
    }

    private static bool IsAmbiguousInterface(
        string via,
        Dictionary<string, ServiceResolution> bySignature,
        Dictionary<string, ServiceResolution?> byName,
        HashSet<string> ambiguousInterfaces)
    {
        if (string.IsNullOrWhiteSpace(via))
            return false;

        if (ambiguousInterfaces.Contains(via))
            return true;

        if (bySignature.TryGetValue(via, out var sigSr))
            return sigSr.IsAmbiguous || string.IsNullOrWhiteSpace(sigSr.Implementation);

        if (byName.TryGetValue(via, out var nameSr) && nameSr is not null)
            return nameSr.IsAmbiguous || string.IsNullOrWhiteSpace(nameSr.Implementation);

        return false;
    }

    private static bool TryResolveTargetType(
        string rawTarget,
        Dictionary<string, ServiceResolution> bySignature,
        Dictionary<string, ServiceResolution?> byName,
        out string resolved)
    {
        if (bySignature.TryGetValue(rawTarget, out var sigSr))
        {
            if (sigSr.IsAmbiguous || string.IsNullOrWhiteSpace(sigSr.Implementation))
            {
                resolved = string.Empty;
                return false;
            }
            resolved = sigSr.Implementation;
            return true;
        }

        if (byName.TryGetValue(rawTarget, out var nameSr) && nameSr is not null)
        {
            if (nameSr.IsAmbiguous || string.IsNullOrWhiteSpace(nameSr.Implementation))
            {
                resolved = string.Empty;
                return false;
            }
            resolved = nameSr.Implementation;
            return true;
        }

        resolved = rawTarget;
        return true;
    }

    private static string? InferProjectName(AnalysisResult result, SourceLocation? location)
    {
        if (location is null || string.IsNullOrEmpty(result.RootPath))
            return null;

        var fullPath = Path.Combine(result.RootPath, location.File.Replace('/', Path.DirectorySeparatorChar));
        return ProjectLocator.FindOwningProject(result.Projects, fullPath)?.Name;
    }

    private static List<List<string>> FindStronglyConnectedComponents(
        string[] vertices,
        Dictionary<string, List<string>> adjacency)
    {
        var index = 0;
        var indices = new Dictionary<string, int>(vertices.Length, StringComparer.Ordinal);
        var lowLink = new Dictionary<string, int>(vertices.Length, StringComparer.Ordinal);
        var onStack = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        var result = new List<List<string>>();

        void StrongConnect(string v)
        {
            indices[v] = index;
            lowLink[v] = index;
            index++;
            stack.Push(v);
            onStack.Add(v);

            foreach (var w in adjacency[v])
            {
                if (!indices.TryGetValue(w, out var wIndex))
                {
                    StrongConnect(w);
                    if (lowLink[w] < lowLink[v])
                        lowLink[v] = lowLink[w];
                }
                else if (onStack.Contains(w) && wIndex < lowLink[v])
                {
                    lowLink[v] = wIndex;
                }
            }

            if (lowLink[v] == indices[v])
            {
                var component = new List<string>();
                string w;
                do
                {
                    w = stack.Pop();
                    onStack.Remove(w);
                    component.Add(w);
                }
                while (!string.Equals(w, v, StringComparison.Ordinal));

                result.Add(component);
            }
        }

        foreach (var v in vertices)
        {
            if (!indices.ContainsKey(v))
                StrongConnect(v);
        }

        return result;
    }

    private static void EnumerateElementaryCycles(
        List<string> scc,
        Dictionary<string, List<string>> adjacency,
        Dictionary<(string From, string To), (string? ProjectName, SourceLocation? Location)> edgeInfo,
        Dictionary<string, (string? ProjectName, SourceLocation? Location)> typeMetadata,
        HashSet<string> reportedCycles,
        List<CheckFinding> findings)
    {
        var rank = new Dictionary<string, int>(scc.Count, StringComparer.Ordinal);
        for (var i = 0; i < scc.Count; i++)
            rank[scc[i]] = i;

        var sccCycles = 0;
        var path = new List<string>();
        var blocked = new HashSet<string>(StringComparer.Ordinal);
        var blockedMap = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        void Unblock(string u)
        {
            blocked.Remove(u);
            if (!blockedMap.TryGetValue(u, out var dependents) || dependents.Count == 0)
                return;

            var copy = dependents.ToArray();
            dependents.Clear();
            foreach (var dep in copy)
            {
                if (blocked.Contains(dep))
                    Unblock(dep);
            }
        }

        bool Circuit(string v, string start, int startRank)
        {
            var foundCycle = false;
            path.Add(v);
            blocked.Add(v);

            foreach (var w in adjacency[v])
            {
                if (sccCycles >= MaxCyclesPerScc)
                    break;
                if (!rank.TryGetValue(w, out var wRank) || wRank < startRank)
                    continue;

                if (string.Equals(w, start, StringComparison.Ordinal))
                {
                    RecordCycle(path, edgeInfo, typeMetadata, reportedCycles, findings);
                    sccCycles++;
                    foundCycle = true;
                }
                else if (!blocked.Contains(w) && path.Count < MaxCycleLength)
                {
                    if (Circuit(w, start, startRank))
                        foundCycle = true;
                }
            }

            if (foundCycle)
            {
                Unblock(v);
            }
            else
            {
                foreach (var w in adjacency[v])
                {
                    if (!rank.TryGetValue(w, out var wRank) || wRank < startRank)
                        continue;

                    if (!blockedMap.TryGetValue(w, out var deps))
                        blockedMap[w] = deps = new HashSet<string>(StringComparer.Ordinal);
                    deps.Add(v);
                }
            }

            path.RemoveAt(path.Count - 1);
            return foundCycle;
        }

        for (var startIndex = 0; startIndex < scc.Count && sccCycles < MaxCyclesPerScc; startIndex++)
        {
            path.Clear();
            blocked.Clear();
            blockedMap.Clear();
            Circuit(scc[startIndex], scc[startIndex], startIndex);
        }
    }

    private static void RecordCycle(
        List<string> ring,
        Dictionary<(string From, string To), (string? ProjectName, SourceLocation? Location)> edgeInfo,
        Dictionary<string, (string? ProjectName, SourceLocation? Location)> typeMetadata,
        HashSet<string> reportedCycles,
        List<CheckFinding> findings)
    {
        if (ring.Count < 2)
            return;

        var minIndex = 0;
        for (var i = 1; i < ring.Count; i++)
        {
            if (string.Compare(ring[i], ring[minIndex], StringComparison.Ordinal) < 0)
                minIndex = i;
        }

        var canonical = new List<string>(ring.Count + 1);
        for (var i = 0; i < ring.Count; i++)
            canonical.Add(ring[(minIndex + i) % ring.Count]);
        canonical.Add(canonical[0]);

        var cycleKey = string.Join(" -> ", canonical);
        if (!reportedCycles.Add(cycleKey))
            return;

        string? projectName = null;
        SourceLocation? location = null;
        if (edgeInfo.TryGetValue((canonical[0], canonical[1]), out var firstEdge))
        {
            projectName = firstEdge.ProjectName;
            location = firstEdge.Location;
        }

        if ((projectName is null || location is null) &&
            typeMetadata.TryGetValue(canonical[0], out var meta))
        {
            projectName ??= meta.ProjectName;
            location ??= meta.Location;
        }

        findings.Add(new CheckFinding(
            CheckSeverity.Warning,
            ArchitectureChecker.CircularServiceDependencyCode,
            $"Circular service dependency detected: {cycleKey}. Extract a shared abstraction, introduce an event boundary, or invert one dependency to break the cycle.",
            Target: cycleKey,
            Location: location,
            ProjectName: projectName));
    }
}
