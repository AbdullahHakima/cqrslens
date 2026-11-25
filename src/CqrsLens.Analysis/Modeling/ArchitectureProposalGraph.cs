namespace CqrsLens;

/// <summary>Temporary project reference graph for evaluating an architecture proposal.</summary>
internal sealed class ArchitectureProposalGraph
{
    private readonly Dictionary<ProjectInfo, HashSet<ProjectInfo>> references;
    private readonly Dictionary<ProjectInfo, HashSet<ProjectInfo>> incoming;
    private readonly Dictionary<ProjectInfo, string> unresolvedReferences = new();

    internal ArchitectureProposalGraph(
        IReadOnlyList<ProjectInfo> scannedProjects,
        IReadOnlyDictionary<string, List<ProjectInfo>> projectsByName)
    {
        var allProjects = projectsByName.Values.SelectMany(projects => projects).Distinct().ToArray();
        references = allProjects.ToDictionary(project => project, _ => new HashSet<ProjectInfo>());
        incoming = allProjects.ToDictionary(project => project, _ => new HashSet<ProjectInfo>());

        var byPath = scannedProjects.Where(project => !string.IsNullOrEmpty(project.FilePath))
            .ToDictionary(project => Path.GetFullPath(project.FilePath), StringComparer.OrdinalIgnoreCase);

        foreach (var project in scannedProjects)
        {
            if (project.ReferenceIncludes.Count > 0)
            {
                // The original paths identify targets even when project names collide.
                foreach (var include in project.ReferenceIncludes)
                {
                    var path = ProjectReferenceResolver.ResolvePath(project.DirectoryPath, include);
                    if (path is not null && byPath.TryGetValue(path, out var target))
                        Add(project, target);
                    else
                        unresolvedReferences.TryAdd(project, include);
                }

                continue;
            }

            foreach (var name in project.ReferenceNames)
            {
                if (projectsByName.TryGetValue(name, out var candidates) && candidates.Count == 1 &&
                    !string.IsNullOrEmpty(candidates[0].FilePath))
                    Add(project, candidates[0]);
                else
                    unresolvedReferences.TryAdd(project, name);
            }
        }
    }

    internal bool Add(ProjectInfo from, ProjectInfo to)
    {
        if (ReferenceEquals(from, to))
            return false;

        if (!references[from].Add(to))
            return false;

        incoming[to].Add(from);
        return true;
    }

    internal bool Remove(ProjectInfo from, ProjectInfo to)
    {
        if (!references[from].Remove(to))
            return false;

        incoming[to].Remove(from);
        return true;
    }

    internal IReadOnlyCollection<ProjectInfo> GetOutgoing(ProjectInfo project) => references[project];

    internal IReadOnlyCollection<ProjectInfo> GetIncoming(ProjectInfo project) => incoming[project];

    internal int AfferentCount(ProjectInfo project) => incoming[project].Count;

    internal int EfferentCount(ProjectInfo project) => references[project].Count;

    internal bool CanReach(ProjectInfo start, ProjectInfo destination)
    {
        if (ReferenceEquals(start, destination))
            return true;

        var visited = new HashSet<ProjectInfo> { start };
        var queue = new Queue<ProjectInfo>();
        queue.Enqueue(start);

        while (queue.TryDequeue(out var current))
        {
            foreach (var next in references[current])
            {
                if (ReferenceEquals(next, destination))
                    return true;
                if (visited.Add(next))
                    queue.Enqueue(next);
            }
        }

        return false;
    }

    internal IReadOnlyList<ProjectInfo> GetTransitiveDependents(ProjectInfo target)
    {
        var visited = new HashSet<ProjectInfo> { target };
        var queue = new Queue<ProjectInfo>();
        queue.Enqueue(target);

        while (queue.TryDequeue(out var current))
        {
            foreach (var caller in incoming[current])
            {
                if (visited.Add(caller))
                    queue.Enqueue(caller);
            }
        }

        visited.Remove(target);
        return visited.ToArray();
    }

    internal IReadOnlyDictionary<ProjectInfo, IReadOnlyList<ProjectInfo>> FindShortestPathsTo(ProjectInfo destination)
    {
        var paths = new Dictionary<ProjectInfo, IReadOnlyList<ProjectInfo>>
        {
            [destination] = [destination],
        };
        var queue = new Queue<ProjectInfo>();
        queue.Enqueue(destination);

        while (queue.TryDequeue(out var current))
        {
            var currentPath = paths[current];
            foreach (var caller in incoming[current]
                .OrderBy(project => project.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(project => project.FilePath, StringComparer.Ordinal))
            {
                if (paths.ContainsKey(caller))
                    continue;

                var callerPath = new List<ProjectInfo>(currentPath.Count + 1) { caller };
                callerPath.AddRange(currentPath);
                paths[caller] = callerPath;
                queue.Enqueue(caller);
            }
        }

        return paths;
    }

    internal IReadOnlyList<ProjectInfo>? FindShortestPathAvoiding(
        ProjectInfo start,
        ProjectInfo destination,
        IReadOnlySet<ProjectInfo> excluded)
    {
        if (excluded.Contains(start) || excluded.Contains(destination))
            return null;

        if (ReferenceEquals(start, destination))
            return [start];

        var parent = new Dictionary<ProjectInfo, ProjectInfo?> { [start] = null };
        var queue = new Queue<ProjectInfo>();
        queue.Enqueue(start);

        while (queue.TryDequeue(out var current))
        {
            foreach (var next in references[current]
                .OrderBy(project => project.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(project => project.FilePath, StringComparer.Ordinal))
            {
                if (excluded.Contains(next) || !parent.TryAdd(next, current))
                    continue;

                if (ReferenceEquals(next, destination))
                {
                    var path = new List<ProjectInfo>();
                    for (ProjectInfo? step = next; step is not null; step = parent[step])
                        path.Add(step);
                    path.Reverse();
                    return path;
                }

                queue.Enqueue(next);
            }
        }

        return null;
    }

    internal IReadOnlyDictionary<ProjectInfo, IReadOnlyList<ProjectInfo>> FindShortestPathsFromAvoiding(
        ProjectInfo start,
        IReadOnlySet<ProjectInfo> excluded)
    {
        var paths = new Dictionary<ProjectInfo, IReadOnlyList<ProjectInfo>>();
        if (excluded.Contains(start))
            return paths;

        paths[start] = [start];
        var queue = new Queue<ProjectInfo>();
        queue.Enqueue(start);

        while (queue.TryDequeue(out var current))
        {
            var currentPath = paths[current];
            foreach (var next in references[current]
                .OrderBy(project => project.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(project => project.FilePath, StringComparer.Ordinal))
            {
                if (excluded.Contains(next) || paths.ContainsKey(next))
                    continue;

                var nextPath = new List<ProjectInfo>(currentPath.Count + 1);
                nextPath.AddRange(currentPath);
                nextPath.Add(next);
                paths[next] = nextPath;
                queue.Enqueue(next);
            }
        }

        return paths;
    }

    internal (IReadOnlyList<ProjectInfo>? Path, (ProjectInfo Source, string Reference)? Unresolved, IReadOnlyCollection<ProjectInfo> Visited) FindPathWithVisited(
        ProjectInfo start, ProjectInfo destination)
    {
        var visited = new Dictionary<ProjectInfo, ProjectInfo?> { [start] = null };
        var queue = new Queue<ProjectInfo>();
        queue.Enqueue(start);
        (ProjectInfo Source, string Reference)? unresolved = null;

        while (queue.TryDequeue(out var current))
        {
            if (current == destination)
            {
                var path = new List<ProjectInfo>();
                for (ProjectInfo? step = current; step is not null; step = visited[step])
                    path.Add(step);
                path.Reverse();
                return (path, unresolved, visited.Keys);
            }

            if (unresolved is null && unresolvedReferences.TryGetValue(current, out var reference))
                unresolved = (current, reference);

            foreach (var next in references[current]
                .OrderBy(project => project.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(project => project.FilePath, StringComparer.Ordinal))
            {
                if (visited.TryAdd(next, current))
                    queue.Enqueue(next);
            }
        }

        return (null, unresolved, visited.Keys);
    }
}
