namespace CqrsLens;

/// <summary>
/// Workspace-wide lookup catalog of scanned type declarations grouped by
/// <see cref="ComponentId"/>, preserving distinct identities for namesake types across
/// namespaces, projects, and generic arities while unifying partial declarations.
/// </summary>
internal sealed class ScannedTypeCatalog
{
    private readonly Dictionary<ComponentId, ScannedComponentEntry> _byId = new();
    private readonly Dictionary<CSharpScanner.ClassDecl, ScannedComponentEntry> _byDecl = new();
    private readonly Dictionary<string, List<ScannedComponentEntry>> _byFullName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CSharpScanner.FileScan> _scansByPath;
    private readonly Dictionary<string, SourceMask> _masksByPath;
    private readonly Dictionary<string, string?> _projectByRelativeFile;
    private Dictionary<string, NamespaceMap>? _namespaceMapsByPath;
    private readonly IReadOnlyDictionary<string, SourceLineIndex> _lineIndices;
    private readonly string _rootPath;
    private readonly HashSet<string>? _testProjects;

    public IReadOnlyList<ScannedComponentEntry> Entries { get; }
    public bool HasAnyClasses => _byDecl.Count > 0;
    internal int DiscoveredControllerCount { get; set; }

    public ScannedTypeCatalog(
        IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles,
        AnalysisResult result,
        IReadOnlyDictionary<string, SourceLineIndex>? lineIndices = null)
    {
        _rootPath = result.RootPath;
        _lineIndices = lineIndices ?? SourceLineIndex.BuildLookup(parsedFiles, result.RootPath);
        _scansByPath = new Dictionary<string, CSharpScanner.FileScan>(parsedFiles.Count, StringComparer.OrdinalIgnoreCase);
        _masksByPath = new Dictionary<string, SourceMask>(parsedFiles.Count, StringComparer.OrdinalIgnoreCase);
        _projectByRelativeFile = new Dictionary<string, string?>(parsedFiles.Count, StringComparer.OrdinalIgnoreCase);

        var projects = result.Projects;
        for (var i = 0; i < projects.Count; i++)
        {
            var p = projects[i];
            if (string.Equals(p.Layer, "Tests", StringComparison.OrdinalIgnoreCase))
            {
                _testProjects ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _testProjects.Add(p.Name);
            }
        }

        var entries = new List<ScannedComponentEntry>();
        foreach (var (file, scan, mask) in parsedFiles)
        {
            _scansByPath[file.Path] = scan;
            _masksByPath[file.Path] = mask;
            var lineIndex = _lineIndices.GetValueOrDefault(file.Path);
            var relPath = lineIndex?.RelativePath ?? SourceLocation.Create(file.Path, result.RootPath, file.Code, 0)?.File;
            if (relPath is not null)
                _projectByRelativeFile.TryAdd(relPath, file.ProjectName);

            foreach (var declaration in scan.Classes)
            {
                var arity = declaration.TypeParameters.Count;
                var componentId = result.IdentityResolver.ResolveComponentId(
                    file.ProjectName,
                    declaration.Namespace,
                    declaration.ContainingTypes,
                    declaration.Name,
                    arity);

                if (!_byId.TryGetValue(componentId, out var entry))
                {
                    entry = new ScannedComponentEntry(
                        componentId,
                        declaration.FullName,
                        declaration.Name,
                        arity,
                        file.ProjectName,
                        IsTestProject(file.ProjectName));
                    _byId[componentId] = entry;
                    entries.Add(entry);

                    if (!_byFullName.TryGetValue(declaration.FullName, out var list))
                    {
                        list = new List<ScannedComponentEntry>();
                        _byFullName[declaration.FullName] = list;
                    }
                    list.Add(entry);
                }

                var location = lineIndex is not null
                    ? lineIndex.Create(declaration.NameIndex)
                    : SourceLocation.Create(file.Path, result.RootPath, file.Code, declaration.NameIndex);
                entry.AddDeclaration(declaration, file, mask, location);
                _byDecl[declaration] = entry;
            }
        }

        Entries = entries;
    }

    public ScannedComponentEntry? TryGetById(ComponentId id)
        => _byId.GetValueOrDefault(id);

    public SourceLocation? CreateLocation(SourceFile file, int index)
        => _lineIndices.TryGetValue(file.Path, out var lineIndex)
            ? lineIndex.Create(index)
            : SourceLocation.Create(file.Path, _rootPath, file.Code, index);

    public bool IsTestProject(string? projectName)
        => _testProjects is not null && !string.IsNullOrWhiteSpace(projectName) && _testProjects.Contains(projectName);

    public string? GetProjectForLocation(SourceLocation? location)
        => location is not null && _projectByRelativeFile.TryGetValue(location.File, out var projectName)
            ? projectName
            : null;

    public bool IsTestProjectForLocation(SourceLocation? location, bool fallback)
    {
        var projectName = GetProjectForLocation(location);
        return projectName is not null ? IsTestProject(projectName) : fallback;
    }

    public ScannedComponentEntry? GetByDeclaration(CSharpScanner.ClassDecl declaration)
        => _byDecl.GetValueOrDefault(declaration);

    public IReadOnlyList<CSharpScanner.ClassDecl> GetScanClasses(string filePath)
        => _scansByPath.TryGetValue(filePath, out var scan)
            ? scan.Classes
            : Array.Empty<CSharpScanner.ClassDecl>();

    public CSharpScanner.FileScan? GetScan(string filePath)
        => _scansByPath.GetValueOrDefault(filePath);

    /// <summary>Resolves an explicit registration's target type to a scanned concrete component.</summary>
    public ScannedComponentEntry? ResolveExplicitRegistration(
        ExplicitServiceRegistration registration,
        TypeHierarchy hierarchy)
    {
        var file = registration.File;
        if (!_scansByPath.TryGetValue(file.Path, out var scan) ||
            !_masksByPath.TryGetValue(file.Path, out var mask))
            return null;

        _namespaceMapsByPath ??= new Dictionary<string, NamespaceMap>(StringComparer.OrdinalIgnoreCase);
        if (!_namespaceMapsByPath.TryGetValue(file.Path, out var namespaceMap))
        {
            namespaceMap = NamespaceMap.Build(file.Code, mask);
            _namespaceMapsByPath[file.Path] = namespaceMap;
        }

        var currentNamespace = namespaceMap.NamespaceAt(registration.SourceIndex);
        var resolvedDecl = hierarchy.TryResolveClass(
            registration.TypeName,
            currentNamespace,
            scan.Usings,
            Array.Empty<string>(),
            registration.Arity,
            out _);

        return resolvedDecl is null ? null : GetByDeclaration(resolvedDecl);
    }

    /// <summary>
    /// Resolves a scanned concrete component by full name, using optional source location,
    /// generic arity, and project name only as deterministic disambiguators when multiple
    /// matching entries exist.
    /// </summary>
    public ScannedComponentEntry? ResolveByFullName(
        string fullName,
        string? preferredProject = null,
        SourceLocation? preferredLocation = null,
        int? preferredArity = null)
    {
        if (!_byFullName.TryGetValue(fullName, out var candidates))
            return null;

        if (candidates.Count == 1)
        {
            var single = candidates[0];
            return single.IsConcrete ? single : null;
        }

        var matches = new List<ScannedComponentEntry>(candidates.Count);
        for (var i = 0; i < candidates.Count; i++)
        {
            if (candidates[i].IsConcrete)
                matches.Add(candidates[i]);
        }
        if (matches.Count == 0) return null;
        if (matches.Count == 1) return matches[0];

        if (preferredLocation is not null)
        {
            var locMatches = new List<ScannedComponentEntry>(matches.Count);
            for (var i = 0; i < matches.Count; i++)
            {
                var m = matches[i];
                for (var d = 0; d < m.Declarations.Count; d++)
                {
                    var loc = m.Declarations[d].Location;
                    if (loc is not null &&
                        string.Equals(loc.File, preferredLocation.File, StringComparison.OrdinalIgnoreCase))
                    {
                        locMatches.Add(m);
                        break;
                    }
                }
            }
            if (locMatches.Count == 1) return locMatches[0];
            if (locMatches.Count > 0) matches = locMatches;
        }

        if (preferredArity.HasValue)
        {
            var arityMatches = new List<ScannedComponentEntry>(matches.Count);
            for (var i = 0; i < matches.Count; i++)
            {
                if (matches[i].GenericArity == preferredArity.Value)
                    arityMatches.Add(matches[i]);
            }
            if (arityMatches.Count == 1) return arityMatches[0];
            if (arityMatches.Count > 0) matches = arityMatches;
        }

        if (!string.IsNullOrWhiteSpace(preferredProject))
        {
            var projMatches = new List<ScannedComponentEntry>(matches.Count);
            for (var i = 0; i < matches.Count; i++)
            {
                if (string.Equals(matches[i].ProjectName, preferredProject, StringComparison.OrdinalIgnoreCase))
                    projMatches.Add(matches[i]);
            }
            if (projMatches.Count == 1) return projMatches[0];
            if (projMatches.Count > 0) matches = projMatches;
        }

        return matches.Count == 1 ? matches[0] : null;
    }
}
