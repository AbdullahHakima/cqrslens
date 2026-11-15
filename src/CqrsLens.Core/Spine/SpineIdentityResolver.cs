namespace CqrsLens;

/// <summary>
/// Resolves legacy shape names and project names to stable <see cref="ArchitectureSpine"/>
/// identities and typed <see cref="NodeReference"/> values.
/// </summary>
internal sealed class SpineIdentityResolver
{
    private readonly AnalysisResult _result;

    private long _projectLookupStamp = -1;
    private Dictionary<string, ProjectId>? _projectsExact;
    private Dictionary<string, ProjectId>? _projectsIgnoreCase;
    private long _projectDirLookupStamp = -1;
    private ProjectId? _rootDirProject;
    private ProjectDirTrieNode? _dirTrieRoot;

    private int _componentLookupVersion = -1;
    private Dictionary<string, List<ComponentNode>>? _componentsByFullName;
    private Dictionary<string, List<ComponentNode>>? _componentsByQualifiedName;

    private int _endpointLookupVersion = -1;
    private Dictionary<string, List<EndpointNode>>? _endpointsByName;

    internal SpineIdentityResolver(AnalysisResult result)
    {
        _result = result;
    }

    internal ProjectId ResolveProjectId(string? projectName)
    {
        if (!string.IsNullOrWhiteSpace(projectName))
        {
            if (_result.Projects.Count < 16)
            {
                foreach (var project in _result.Projects)
                {
                    if (string.Equals(project.Name, projectName, StringComparison.Ordinal))
                        return EnsureProjectId(project);
                }

                foreach (var project in _result.Projects)
                {
                    if (string.Equals(project.Name, projectName, StringComparison.OrdinalIgnoreCase))
                        return EnsureProjectId(project);
                }

                return SyntheticProjectId(projectName);
            }

            EnsureProjectLookup();
            if (_projectsExact!.TryGetValue(projectName, out var exact))
                return exact;
            if (_projectsIgnoreCase!.TryGetValue(projectName, out var ic))
                return ic;

            return SyntheticProjectId(projectName);
        }

        return SyntheticProjectId(null);
    }

    private void EnsureProjectLookup()
    {
        var stamp = ((long)_result.Spine.ProjectsVersion << 20) ^ _result.Projects.Count;
        if (_projectsExact is not null && _projectLookupStamp == stamp)
            return;

        var count = _result.Projects.Count;
        var exact = new Dictionary<string, ProjectId>(count, StringComparer.Ordinal);
        var ic = new Dictionary<string, ProjectId>(count, StringComparer.OrdinalIgnoreCase);

        foreach (var project in _result.Projects)
        {
            var id = EnsureProjectId(project);
            if (!string.IsNullOrWhiteSpace(project.Name))
            {
                exact.TryAdd(project.Name, id);
                ic.TryAdd(project.Name, id);
            }
        }

        _projectsExact = exact;
        _projectsIgnoreCase = ic;
        _projectLookupStamp = stamp;
    }

    private void EnsureProjectDirLookup()
    {
        var stamp = ((long)_result.Spine.ProjectsVersion << 20) ^ _result.Projects.Count;
        if (_projectDirLookupStamp == stamp)
            return;

        _rootDirProject = null;
        if (string.IsNullOrWhiteSpace(_result.RootPath))
        {
            _dirTrieRoot = null;
            _projectDirLookupStamp = stamp;
            return;
        }

        ProjectDirTrieNode? root = null;
        foreach (var project in _result.Projects)
        {
            if (string.IsNullOrWhiteSpace(project.DirectoryPath))
                continue;

            var relDir = Path.GetRelativePath(_result.RootPath, project.DirectoryPath);
            if (relDir.Contains('\\', StringComparison.Ordinal))
                relDir = relDir.Replace('\\', '/');
            if (relDir == "." || relDir.Length == 0)
            {
                _rootDirProject ??= EnsureProjectId(project);
                continue;
            }

            root ??= new ProjectDirTrieNode("");
            var current = root;
            var span = relDir.AsSpan();
            while (true)
            {
                var slash = span.IndexOf('/');
                var seg = slash >= 0 ? span[..slash] : span;
                current = current.GetOrAddChild(seg);
                if (slash < 0)
                    break;
                span = span[(slash + 1)..];
            }

            current.ProjectId ??= EnsureProjectId(project);
        }

        root?.SortRecursive();
        _dirTrieRoot = root;
        _projectDirLookupStamp = stamp;
    }

    private void EnsureComponentLookup()
    {
        var ver = _result.Spine.ComponentsVersion;
        if (_componentsByFullName is not null && _componentLookupVersion == ver)
            return;

        var byFull = new Dictionary<string, List<ComponentNode>>(StringComparer.Ordinal);
        var byQual = new Dictionary<string, List<ComponentNode>>(StringComparer.Ordinal);
        foreach (var component in _result.Spine.Components)
        {
            if (!byFull.TryGetValue(component.FullName, out var fullList))
                byFull[component.FullName] = fullList = new List<ComponentNode>(1);
            fullList.Add(component);

            var qual = component.Id.Symbol.QualifiedName;
            if (!byQual.TryGetValue(qual, out var qualList))
                byQual[qual] = qualList = new List<ComponentNode>(1);
            qualList.Add(component);
        }

        _componentsByFullName = byFull;
        _componentsByQualifiedName = byQual;
        _componentLookupVersion = ver;
    }

    private void EnsureEndpointLookup()
    {
        var ver = _result.Spine.EndpointsVersion;
        if (_endpointsByName is not null && _endpointLookupVersion == ver)
            return;

        var byName = new Dictionary<string, List<EndpointNode>>(StringComparer.Ordinal);
        foreach (var ep in _result.Spine.Endpoints)
        {
            if (!byName.TryGetValue(ep.Name, out var list))
                byName[ep.Name] = list = new List<EndpointNode>(1);
            list.Add(ep);
        }

        _endpointsByName = byName;
        _endpointLookupVersion = ver;
    }

    internal ProjectId EnsureProjectId(ProjectInfo project)
    {
        if (project.SpineId is not null)
            return project.SpineId;

        ProjectId id;
        if (!string.IsNullOrWhiteSpace(project.FilePath) && !string.IsNullOrWhiteSpace(_result.RootPath))
        {
            id = ProjectId.FromProjectFile(project.FilePath, _result.RootPath);
        }
        else if (!string.IsNullOrWhiteSpace(project.Name))
        {
            id = ProjectId.FromRelativePath($"__project__/{SafeSegment(project.Name)}");
        }
        else
        {
            id = SyntheticProjectId(null);
        }

        project.SpineId = id;
        return id;
    }

    internal static ProjectId SyntheticProjectId(string? projectName)
        => ProjectId.FromRelativePath($"__synthetic__/{SafeSegment(projectName)}");

    internal ComponentId ResolveComponentId(
        string? projectName,
        string? ns,
        IReadOnlyList<string>? containingTypes,
        string name,
        int genericArity = 0)
    {
        var projectId = ResolveProjectId(projectName);
        var symbol = SymbolId.Create(projectId, ns, containingTypes, name, genericArity);
        return ComponentId.FromSymbol(symbol);
    }

    internal ComponentNode? FindComponentByFullName(
        string fullName,
        ComponentRole? preferredRole = null,
        string? preferredProject = null)
    {
        ComponentNode? fallback = null;
        ComponentNode? roleMatch = null;
        if (_result.Spine.ComponentsCount < 24)
        {
            foreach (var component in _result.Spine.Components)
            {
                if (!string.Equals(component.FullName, fullName, StringComparison.Ordinal) &&
                    !string.Equals(component.Id.Symbol.QualifiedName, fullName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (TrySelectComponent(component, preferredRole, preferredProject, ref fallback, ref roleMatch))
                    return component;
            }

            return roleMatch ?? fallback;
        }

        EnsureComponentLookup();
        _componentsByFullName!.TryGetValue(fullName, out var fullMatches);
        _componentsByQualifiedName!.TryGetValue(fullName, out var qualMatches);
        if (fullMatches is null && qualMatches is null)
            return null;

        if (fullMatches is not null && qualMatches is not null && !ReferenceEquals(fullMatches, qualMatches))
        {
            // Preserve Components sorted-Id order across both match lists when distinct.
            foreach (var component in fullMatches.Concat(qualMatches).Distinct().OrderBy(c => c.Id))
            {
                if (TrySelectComponent(component, preferredRole, preferredProject, ref fallback, ref roleMatch))
                    return component;
            }
        }
        else
        {
            var list = fullMatches ?? qualMatches!;
            for (var i = 0; i < list.Count; i++)
            {
                var component = list[i];
                if (TrySelectComponent(component, preferredRole, preferredProject, ref fallback, ref roleMatch))
                    return component;
            }
        }

        return roleMatch ?? fallback;
    }

    private bool TrySelectComponent(
        ComponentNode component,
        ComponentRole? preferredRole,
        string? preferredProject,
        ref ComponentNode? fallback,
        ref ComponentNode? roleMatch)
    {
        fallback ??= component;
        var hasRole = preferredRole is null || component.HasRole(preferredRole.Value);
        if (!hasRole)
            return false;

        roleMatch ??= component;
        return preferredProject is not null &&
               _result.Spine.TryGetProject(component.Project, out var proj) &&
               string.Equals(proj!.Name, preferredProject, StringComparison.Ordinal);
    }

    internal NodeReference ResolveComponentOrExternalRef(
        string fullName,
        ComponentRole? preferredRole = null,
        string? preferredProject = null)
    {
        var component = FindComponentByFullName(fullName, preferredRole, preferredProject);
        return component is not null
            ? NodeReference.ForComponent(component.Id)
            : NodeReference.ForExternal(fullName);
    }

    internal NodeReference ResolveEndpointSourceRef(
        string endpointName,
        string? projectName,
        SourceLocation? location)
    {
        EndpointNode? best = null;
        if (_result.Spine.EndpointsCount < 24)
        {
            foreach (var ep in _result.Spine.Endpoints)
            {
                if (!string.Equals(ep.Name, endpointName, StringComparison.Ordinal))
                    continue;

                best ??= ep;
                if (location is not null && Equals(ep.Location, location))
                    return NodeReference.ForEndpoint(ep.Id);

                if (projectName is not null &&
                    _result.Spine.TryGetProject(ep.Project, out var proj) &&
                    string.Equals(proj!.Name, projectName, StringComparison.Ordinal))
                {
                    best = ep;
                }
            }

            return best is not null
                ? NodeReference.ForEndpoint(best.Id)
                : NodeReference.ForExternal(endpointName);
        }

        EnsureEndpointLookup();
        if (!_endpointsByName!.TryGetValue(endpointName, out var candidates))
            return NodeReference.ForExternal(endpointName);

        for (var i = 0; i < candidates.Count; i++)
        {
            var ep = candidates[i];
            best ??= ep;
            if (location is not null && Equals(ep.Location, location))
                return NodeReference.ForEndpoint(ep.Id);

            if (projectName is not null &&
                _result.Spine.TryGetProject(ep.Project, out var proj) &&
                string.Equals(proj!.Name, projectName, StringComparison.Ordinal))
            {
                best = ep;
            }
        }

        return best is not null
            ? NodeReference.ForEndpoint(best.Id)
            : NodeReference.ForExternal(endpointName);
    }

    internal NodeReference ResolveInterfaceRef(
        string interfaceSignatureOrName,
        SourceLocation? interfaceLocation = null)
    {
        var projectId = ResolveProjectForLocation(interfaceLocation);
        if (projectId is not null && TryParseTypeSignature(interfaceSignatureOrName, out var ns, out var name, out var arity, out var args))
        {
            var symbol = SymbolId.Create(projectId, ns, null, name, arity, args);
            return NodeReference.ForInterface(symbol);
        }

        return NodeReference.ForExternalInterface(interfaceSignatureOrName);
    }

    internal NodeReference ResolveMethodTargetRef(
        string declaringTypeFullName,
        string methodName,
        string? projectName = null)
    {
        var comp = FindComponentByFullName(declaringTypeFullName, null, projectName);
        if (comp is not null)
            return NodeReference.ForMethod(comp.Symbol, methodName);

        if (TryFindClassLocationProject(declaringTypeFullName, out var projId) &&
            TryParseTypeSignature(declaringTypeFullName, out var ns, out var name, out var arity, out var args))
        {
            return NodeReference.ForMethod(SymbolId.Create(projId, ns, null, name, arity, args), methodName);
        }

        return NodeReference.ForExternalMethod(declaringTypeFullName, methodName);
    }

    internal NodeReference ResolveTypeOrComponentRef(string typeFullName, string? projectName = null)
    {
        var comp = FindComponentByFullName(typeFullName, null, projectName);
        if (comp is not null)
            return NodeReference.ForComponent(comp.Id);

        if (TryFindClassLocationProject(typeFullName, out var projId) &&
            TryParseTypeSignature(typeFullName, out var ns, out var name, out var arity, out var args))
        {
            return NodeReference.ForType(SymbolId.Create(projId, ns, null, name, arity, args));
        }

        return NodeReference.ForExternal(typeFullName);
    }

    internal NodeReference ResolveTouchesDataTarget(string label, string? projectName)
    {
        var parenOpen = label.IndexOf(" (", StringComparison.Ordinal);
        if (parenOpen > 0 && label.EndsWith(')'))
        {
            var prop = label[..parenOpen].Trim();
            var entitySimple = label[(parenOpen + 2)..^1].Trim();
            if (prop.Length > 0 && entitySimple.Length > 0)
            {
                foreach (var ctx in _result.DbContexts)
                {
                    if (projectName is not null && !string.Equals(ctx.ProjectName, projectName, StringComparison.Ordinal))
                        continue;
                    if (ctx.DbSets.Any(s =>
                            string.Equals(s.Property, prop, StringComparison.Ordinal) &&
                            string.Equals(SimpleNameOf(s.Entity), entitySimple, StringComparison.Ordinal)))
                    {
                        var ctxComp = FindComponentByFullName(ctx.FullName, ComponentRole.DbContext, ctx.ProjectName);
                        if (ctxComp is not null)
                            return NodeReference.ForMember(ctxComp.Symbol, prop);
                    }
                }

                foreach (var ctx in _result.DbContexts)
                {
                    if (ctx.DbSets.Any(s =>
                            string.Equals(s.Property, prop, StringComparison.Ordinal) &&
                            string.Equals(SimpleNameOf(s.Entity), entitySimple, StringComparison.Ordinal)))
                    {
                        var ctxComp = FindComponentByFullName(ctx.FullName, ComponentRole.DbContext, ctx.ProjectName);
                        if (ctxComp is not null)
                            return NodeReference.ForMember(ctxComp.Symbol, prop);
                    }
                }

                return NodeReference.ForExternalMember($"DbSet<{entitySimple}>", prop);
            }
        }

        // Plain entity name or custom label
        ComponentNode? entityMatch = null;
        var multiple = false;
        foreach (var comp in _result.Spine.Components)
        {
            if (!comp.HasRole(ComponentRole.Entity))
                continue;
            if (string.Equals(comp.FullName, label, StringComparison.Ordinal))
                return NodeReference.ForComponent(comp.Id);
            if (!label.Contains('.') && string.Equals(comp.Name, label, StringComparison.Ordinal))
            {
                if (entityMatch is null)
                    entityMatch = comp;
                else
                    multiple = true;
            }
        }

        if (entityMatch is not null && !multiple)
            return NodeReference.ForComponent(entityMatch.Id);

        return NodeReference.ForExternal(label);
    }

    internal string FormatTouchesDataTarget(NodeReference target)
    {
        if (target.Kind == NodeReferenceKind.Component && target.ComponentId is not null)
        {
            if (_result.Spine.TryGetComponent(target.ComponentId, out var comp) && comp!.HasRole(ComponentRole.Entity))
                return comp.Name;
            return target.Name;
        }

        if (target.Kind == NodeReferenceKind.Member)
        {
            if (target.SymbolId is not null)
            {
                foreach (var ctx in _result.DbContexts)
                {
                    var ctxCompId = ctx.SpineId ?? ResolveComponentId(ctx.ProjectName, ctx.Namespace, ctx.ContainingTypes, ctx.Name);
                    if (ctxCompId.Symbol.Equals(target.SymbolId))
                    {
                        var set = ctx.DbSets.FirstOrDefault(s =>
                            string.Equals(s.Property, target.MemberName, StringComparison.Ordinal));
                        if (set is not null)
                            return $"{set.Property} ({SimpleNameOf(set.Entity)})";
                    }
                }
            }

            if (target.MemberName is not null)
            {
                const string prefix = "DbSet<";
                if (target.Name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    var close = target.Name.IndexOf(">.", prefix.Length, StringComparison.Ordinal);
                    if (close > prefix.Length)
                    {
                        var entity = target.Name[prefix.Length..close];
                        return $"{target.MemberName} ({entity})";
                    }
                }
            }
        }

        return target.Name;
    }

    internal string? ProjectNameFor(ProjectId projectId)
        => _result.Spine.TryGetProject(projectId, out var proj) ? proj!.Name : null;

    internal ProjectId? ResolveProjectForLocation(SourceLocation? location)
    {
        if (location is null || string.IsNullOrWhiteSpace(_result.RootPath))
            return null;

        EnsureProjectDirLookup();
        if (_dirTrieRoot is null && _rootDirProject is null)
            return null;

        var normalizedFile = location.File;
        if (normalizedFile.Contains('\\', StringComparison.Ordinal))
            normalizedFile = normalizedFile.Replace('\\', '/');

        var best = _rootDirProject;
        var current = _dirTrieRoot;
        if (current is not null)
        {
            var remaining = normalizedFile.AsSpan();
            while (true)
            {
                var slash = remaining.IndexOf('/');
                if (slash < 0)
                    break;

                current = current.FindChild(remaining[..slash]);
                if (current is null)
                    break;

                if (current.ProjectId is not null)
                    best = current.ProjectId;

                remaining = remaining[(slash + 1)..];
            }
        }

        return best;
    }

    internal static string SimpleNameOf(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return typeName;
        var generic = typeName.IndexOf('<');
        var core = generic >= 0 ? typeName[..generic] : typeName;
        var dot = core.LastIndexOf('.');
        return dot >= 0 ? core[(dot + 1)..] : core;
    }

    internal static EvidenceOccurrence SyntheticOccurrence(
        EvidenceRule rule,
        EvidenceOrigin origin,
        SourceLocation? location,
        EvidenceStrength strength,
        IReadOnlyList<NodeReference>? via = null,
        string? scopeProjectName = null)
        => new(
            rule,
            origin,
            location,
            strength,
            via,
            location is null ? "Synthetic or hand-constructed model entry" : null,
            scopeProjectName: scopeProjectName);

    private bool TryFindClassLocationProject(string fullName, out ProjectId projectId)
    {
        if (_result.Spine.ComponentsCount < 24)
        {
            foreach (var comp in _result.Spine.Components)
            {
                if (string.Equals(comp.FullName, fullName, StringComparison.Ordinal))
                {
                    projectId = comp.Project;
                    return true;
                }
            }

            projectId = null!;
            return false;
        }

        EnsureComponentLookup();
        if (_componentsByFullName!.TryGetValue(fullName, out var matches) && matches.Count > 0)
        {
            projectId = matches[0].Project;
            return true;
        }

        projectId = null!;
        return false;
    }

    private static bool TryParseTypeSignature(
        string signature,
        out string? ns,
        out string name,
        out int arity,
        out string[]? typeArgs)
    {
        ns = null;
        name = "";
        arity = 0;
        typeArgs = null;

        if (string.IsNullOrWhiteSpace(signature))
            return false;

        var raw = signature.Trim();
        var angle = raw.IndexOf('<');
        if (angle > 0 && raw.EndsWith('>'))
        {
            var inner = raw[(angle + 1)..^1];
            typeArgs = inner.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            raw = raw[..angle];
        }

        var tick = raw.LastIndexOf('`');
        if (tick > 0 && int.TryParse(raw[(tick + 1)..], out var parsedArity))
        {
            arity = parsedArity;
            raw = raw[..tick];
        }
        else if (typeArgs is not null)
        {
            arity = typeArgs.Length;
        }

        var dot = raw.LastIndexOf('.');
        if (dot > 0)
        {
            ns = raw[..dot];
            name = raw[(dot + 1)..];
        }
        else
        {
            name = raw;
        }

        return !string.IsNullOrWhiteSpace(name);
    }

    private static string SafeSegment(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "__unassigned__";
        return name.Trim().Replace('/', '_').Replace('\\', '_');
    }

    private sealed class ProjectDirTrieNode
    {
        private List<ProjectDirTrieNode>? _buildChildren;
        private ProjectDirTrieNode[]? _children;

        public string Segment { get; }
        public ProjectId? ProjectId { get; set; }

        public ProjectDirTrieNode(string segment)
        {
            Segment = segment;
        }

        public ProjectDirTrieNode GetOrAddChild(ReadOnlySpan<char> segment)
        {
            _buildChildren ??= new List<ProjectDirTrieNode>(4);
            for (var i = 0; i < _buildChildren.Count; i++)
            {
                var existing = _buildChildren[i];
                if (segment.Equals(existing.Segment, StringComparison.OrdinalIgnoreCase))
                    return existing;
            }

            var created = new ProjectDirTrieNode(segment.ToString());
            _buildChildren.Add(created);
            return created;
        }

        public void SortRecursive()
        {
            if (_buildChildren is null || _buildChildren.Count == 0)
                return;

            _children = _buildChildren.ToArray();
            _buildChildren = null;
            if (_children.Length > 6)
                Array.Sort(_children, static (a, b) => string.Compare(a.Segment, b.Segment, StringComparison.OrdinalIgnoreCase));

            for (var i = 0; i < _children.Length; i++)
                _children[i].SortRecursive();
        }

        public ProjectDirTrieNode? FindChild(ReadOnlySpan<char> segment)
        {
            var children = _children;
            if (children is null)
                return null;

            if (children.Length <= 6)
            {
                for (var i = 0; i < children.Length; i++)
                {
                    if (segment.Equals(children[i].Segment, StringComparison.OrdinalIgnoreCase))
                        return children[i];
                }
                return null;
            }

            var lo = 0;
            var hi = children.Length - 1;
            while (lo <= hi)
            {
                var mid = lo + ((hi - lo) >> 1);
                var cmp = MemoryExtensions.CompareTo(segment, children[mid].Segment.AsSpan(), StringComparison.OrdinalIgnoreCase);
                if (cmp == 0)
                    return children[mid];
                if (cmp < 0)
                    hi = mid - 1;
                else
                    lo = mid + 1;
            }

            return null;
        }
    }
}
