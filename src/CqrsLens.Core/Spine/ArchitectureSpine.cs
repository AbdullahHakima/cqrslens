namespace CqrsLens;

/// <summary>
/// Unified F0 architecture spine: <c>project → component → relation</c>, with explicit
/// evidenced role sets, structured multi-occurrence evidence, per-fact strength, and
/// separate unproven populations (F0 §1–§6).
/// </summary>
public sealed class ArchitectureSpine
{
    private readonly Dictionary<ProjectId, ProjectNode> _projects = new();
    private readonly Dictionary<ComponentId, ComponentNode> _components = new();
    private readonly Dictionary<EndpointId, EndpointNode> _endpoints = new();
    private readonly Dictionary<RelationKey, SpineRelation> _relations = new();
    private readonly List<SpineRelation> _discoveryOrderList = new();
    private readonly HashSet<RelationKey> _explicitFlowKeys = new();
    private readonly HashSet<string> _promotableRefNames = new(StringComparer.Ordinal);
    private readonly HashSet<ProjectId> _syntheticProjects = new();
    private long _nextDiscoverySequence;
    private bool _discoveryOrderDirty;
    private int _version;
    private int _projectsVersion;
    private int _componentsVersion;
    private int _endpointsVersion;

    private ProjectNode[]? _sortedProjects;
    private string[]? _sortedLayers;
    private ComponentNode[]? _sortedComponents;
    private EndpointNode[]? _sortedEndpoints;
    private SpineRelation[]? _sortedRelations;
    private SpineRelation[]? _relationsInDiscoveryOrder;
    private Dictionary<(NodeReference From, RelationKind Kind), List<SpineRelation>>? _outgoingDiscoveryByFromKind;
    private Dictionary<(NodeReference To, RelationKind Kind), List<SpineRelation>>? _incomingDiscoveryByToKind;
    private Dictionary<RelationKind, List<SpineRelation>>? _relationsByKind;
    private Dictionary<NodeReference, List<SpineRelation>>? _outgoingByFrom;
    private Dictionary<(NodeReference From, RelationKind Kind), List<SpineRelation>>? _outgoingByFromKind;
    private Dictionary<NodeReference, List<SpineRelation>>? _incomingByTo;
    private Dictionary<(NodeReference To, RelationKind Kind), List<SpineRelation>>? _incomingByToKind;
    private FlowEdge[]? _cachedFlowEdgesFalse;
    private FlowEdge[]? _cachedFlowEdgesTrue;
    private ResolvedEdge[]? _cachedResolvedEdges;
    private ServiceCallEdge[]? _cachedServiceCallEdges;
    private DispatchCallSite[]? _cachedDispatchCallSites;

    public IReadOnlyList<ProjectNode> Projects =>
        _sortedProjects ??= BuildSortedProjects();

    public IReadOnlyList<string> Layers =>
        _sortedLayers ??= BuildSortedLayers();

    public IReadOnlyList<ComponentNode> Components =>
        _sortedComponents ??= BuildSortedComponents();

    public IReadOnlyList<EndpointNode> Endpoints =>
        _sortedEndpoints ??= BuildSortedEndpoints();

    public IReadOnlyList<SpineRelation> Relations =>
        _sortedRelations ??= BuildSortedRelations();

    internal IReadOnlyList<SpineRelation> RelationsInDiscoveryOrder =>
        _relationsInDiscoveryOrder ??= BuildRelationsInDiscoveryOrder();

    internal IReadOnlyCollection<SpineRelation> UnindexedRelations => _relations.Values;

    private ProjectNode[] BuildSortedProjects()
    {
        if (_projects.Count == 0)
            return Array.Empty<ProjectNode>();
        var arr = new ProjectNode[_projects.Count];
        _projects.Values.CopyTo(arr, 0);
        if (arr.Length > 1)
            Array.Sort(arr, static (a, b) => a.Id.CompareTo(b.Id));
        return arr;
    }

    private string[] BuildSortedLayers()
    {
        if (_projects.Count == 0)
            return Array.Empty<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in _projects.Values)
            seen.Add(p.Layer);
        var arr = new string[seen.Count];
        seen.CopyTo(arr);
        if (arr.Length > 1)
        {
            Array.Sort(arr, static (a, b) =>
            {
                var c = string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
                return c != 0 ? c : string.Compare(a, b, StringComparison.Ordinal);
            });
        }
        return arr;
    }

    private ComponentNode[] BuildSortedComponents()
    {
        if (_components.Count == 0)
            return Array.Empty<ComponentNode>();
        var arr = new ComponentNode[_components.Count];
        _components.Values.CopyTo(arr, 0);
        if (arr.Length > 1)
            Array.Sort(arr, static (a, b) => a.Id.CompareTo(b.Id));
        return arr;
    }

    private EndpointNode[] BuildSortedEndpoints()
    {
        if (_endpoints.Count == 0)
            return Array.Empty<EndpointNode>();
        var arr = new EndpointNode[_endpoints.Count];
        _endpoints.Values.CopyTo(arr, 0);
        if (arr.Length > 1)
            Array.Sort(arr, static (a, b) => a.Id.CompareTo(b.Id));
        return arr;
    }

    private SpineRelation[] BuildSortedRelations()
    {
        if (_relations.Count == 0)
            return Array.Empty<SpineRelation>();
        var arr = new SpineRelation[_relations.Count];
        _relations.Values.CopyTo(arr, 0);
        if (arr.Length > 1)
            Array.Sort(arr, static (a, b) => a.Key.CompareTo(b.Key));
        return arr;
    }

    private List<SpineRelation> GetDiscoveryOrderedList()
    {
        if (_discoveryOrderDirty)
        {
            _discoveryOrderList.Clear();
            foreach (var rel in _relations.Values)
                _discoveryOrderList.Add(rel);
            if (_discoveryOrderList.Count > 1)
                _discoveryOrderList.Sort(static (a, b) => a.DiscoverySequence.CompareTo(b.DiscoverySequence));
            _discoveryOrderDirty = false;
        }
        return _discoveryOrderList;
    }

    private SpineRelation[] BuildRelationsInDiscoveryOrder()
    {
        if (_relations.Count == 0)
            return Array.Empty<SpineRelation>();
        return GetDiscoveryOrderedList().ToArray();
    }

    internal int Version => _version;
    internal int ProjectsVersion => _projectsVersion;
    internal int ComponentsVersion => _componentsVersion;
    internal int EndpointsVersion => _endpointsVersion;
    internal int ComponentsCount => _components.Count;
    internal int EndpointsCount => _endpoints.Count;
    internal bool HasAnySyntheticProjects => _syntheticProjects.Count > 0;

    public UnprovenPopulations Unproven { get; } = new();

    public ProjectNode AddProject(ProjectNode project)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (!_projects.TryAdd(project.Id, project))
            throw new ArgumentException($"Duplicate ProjectId '{project.Id}'.", nameof(project));

        _sortedProjects = null;
        _sortedLayers = null;
        _projectsVersion++;
        InvalidateQueryCaches();
        return project;
    }

    public ComponentNode AddOrMergeComponent(
        ComponentId id,
        SourceLocation? location,
        ComponentRole role,
        EvidenceOccurrence evidence)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(evidence);

        TrackSyntheticProject(id.Project);

        if (_components.TryGetValue(id, out var existing))
        {
            existing.AddRole(role, evidence);
            _componentsVersion++;
            InvalidateQueryCaches();
            return existing;
        }

        var created = new ComponentNode(id, location, role, evidence);
        _components[id] = created;
        _sortedComponents = null;
        _componentsVersion++;
        PromoteReferencesToComponent(id);
        InvalidateQueryCaches();
        return created;
    }

    public EndpointNode AddEndpoint(EndpointNode endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!_endpoints.TryAdd(endpoint.Id, endpoint))
            throw new ArgumentException($"Duplicate EndpointId '{endpoint.Id}'.", nameof(endpoint));

        TrackSyntheticProject(endpoint.Project);
        _sortedEndpoints = null;
        _endpointsVersion++;
        InvalidateQueryCaches();
        return endpoint;
    }

    public SpineRelation AddOrMergeRelation(
        RelationKind kind,
        NodeReference from,
        NodeReference to,
        EvidenceOccurrence occurrence,
        string? method = null)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        ArgumentNullException.ThrowIfNull(occurrence);

        TrackRef(from);
        TrackRef(to);
        foreach (var viaRef in occurrence.Via)
            TrackRef(viaRef);

        var key = new RelationKey(kind, from, to, occurrence.Via, method);
        if (_relations.TryGetValue(key, out var existing))
        {
            existing.AddOccurrence(occurrence);
            InvalidateQueryCaches();
            return existing;
        }

        var relation = new SpineRelation(key, occurrence)
        {
            DiscoverySequence = _nextDiscoverySequence++,
        };
        _relations[key] = relation;
        if (!_discoveryOrderDirty)
            _discoveryOrderList.Add(relation);
        InvalidateRelationCaches();
        return relation;
    }

    internal SpineRelation UpgradeOrMergeRelationOccurrence(
        RelationKind kind,
        NodeReference from,
        NodeReference to,
        EvidenceOccurrence occurrence,
        EvidenceRule? replaceRuleAtSameLocation = null,
        string? method = null)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        ArgumentNullException.ThrowIfNull(occurrence);

        var key = new RelationKey(kind, from, to, occurrence.Via, method);
        if (replaceRuleAtSameLocation is not null &&
            occurrence.Location is not null &&
            _relations.TryGetValue(key, out var existing))
        {
            foreach (var prev in existing.Occurrences)
            {
                if (prev.Rule == replaceRuleAtSameLocation && Equals(prev.Location, occurrence.Location))
                {
                    if (existing.TryReplaceOccurrence(prev, occurrence))
                    {
                        InvalidateRelationCaches();
                        return existing;
                    }
                    break;
                }
            }
        }

        return AddOrMergeRelation(kind, from, to, occurrence, method);
    }

    public bool TryGetProject(ProjectId id, out ProjectNode? project)
        => _projects.TryGetValue(id, out project);

    public bool TryGetComponent(ComponentId id, out ComponentNode? component)
        => _components.TryGetValue(id, out component);

    public bool TryGetEndpoint(EndpointId id, out EndpointNode? endpoint)
        => _endpoints.TryGetValue(id, out endpoint);

    public bool TryGetRelation(RelationKey key, out SpineRelation? relation)
        => _relations.TryGetValue(key, out relation);

    public IReadOnlyList<ProjectNode> GetProjectsInLayer(string layer)
    {
        var projects = Projects;
        List<ProjectNode>? matches = null;
        for (var i = 0; i < projects.Count; i++)
        {
            if (string.Equals(projects[i].Layer, layer, StringComparison.OrdinalIgnoreCase))
                (matches ??= new List<ProjectNode>()).Add(projects[i]);
        }

        return matches is null ? Array.Empty<ProjectNode>() : matches;
    }

    public IReadOnlyList<ComponentNode> GetComponentsByRole(ComponentRole role)
    {
        var components = Components;
        List<ComponentNode>? matches = null;
        for (var i = 0; i < components.Count; i++)
        {
            if (components[i].HasRole(role))
                (matches ??= new List<ComponentNode>()).Add(components[i]);
        }

        return matches is null ? Array.Empty<ComponentNode>() : matches;
    }

    private const int RelationIndexThreshold = 64;

    public IReadOnlyList<SpineRelation> GetRelations(RelationKind kind)
    {
        if (_relationsByKind is not null || _relations.Count >= RelationIndexThreshold)
        {
            EnsureRelationIndexes();
            return _relationsByKind!.TryGetValue(kind, out var list)
                ? list
                : Array.Empty<SpineRelation>();
        }

        List<SpineRelation>? matches = null;
        foreach (var rel in Relations)
        {
            if (rel.Kind == kind)
                (matches ??= new List<SpineRelation>()).Add(rel);
        }

        return matches is null ? Array.Empty<SpineRelation>() : matches;
    }

    public IReadOnlyList<SpineRelation> GetOutgoingRelations(NodeReference from, RelationKind? kind = null)
    {
        if (_relationsByKind is not null || _relations.Count >= RelationIndexThreshold)
        {
            EnsureRelationIndexes();
            if (kind is null)
            {
                return _outgoingByFrom!.TryGetValue(from, out var all)
                    ? all
                    : Array.Empty<SpineRelation>();
            }

            return _outgoingByFromKind!.TryGetValue((from, kind.Value), out var filtered)
                ? filtered
                : Array.Empty<SpineRelation>();
        }

        List<SpineRelation>? matches = null;
        foreach (var rel in Relations)
        {
            if (rel.From.Equals(from) && (kind is null || rel.Kind == kind.Value))
                (matches ??= new List<SpineRelation>()).Add(rel);
        }

        return matches is null ? Array.Empty<SpineRelation>() : matches;
    }

    public IReadOnlyList<SpineRelation> GetIncomingRelations(NodeReference to, RelationKind? kind = null)
    {
        if (_relationsByKind is not null || _relations.Count >= RelationIndexThreshold)
        {
            EnsureRelationIndexes();
            if (kind is null)
            {
                return _incomingByTo!.TryGetValue(to, out var all)
                    ? all
                    : Array.Empty<SpineRelation>();
            }

            return _incomingByToKind!.TryGetValue((to, kind.Value), out var filtered)
                ? filtered
                : Array.Empty<SpineRelation>();
        }

        List<SpineRelation>? matches = null;
        foreach (var rel in Relations)
        {
            if (rel.To.Equals(to) && (kind is null || rel.Kind == kind.Value))
                (matches ??= new List<SpineRelation>()).Add(rel);
        }

        return matches is null ? Array.Empty<SpineRelation>() : matches;
    }

    internal bool HasRelation(RelationKind kind)
    {
        if (_relationsByKind is not null)
            return _relationsByKind.TryGetValue(kind, out var list) && list.Count > 0;

        var listOrdered = GetDiscoveryOrderedList();
        for (var i = 0; i < listOrdered.Count; i++)
        {
            if (listOrdered[i].Kind == kind)
                return true;
        }
        return false;
    }

    internal IReadOnlyList<SpineRelation> GetOutgoingRelationsInDiscoveryOrder(NodeReference from, RelationKind kind)
    {
        if (_outgoingDiscoveryByFromKind is not null || _relations.Count >= RelationIndexThreshold)
        {
            EnsureOutgoingDiscoveryIndex();
            return _outgoingDiscoveryByFromKind!.TryGetValue((from, kind), out var list)
                ? list
                : Array.Empty<SpineRelation>();
        }

        List<SpineRelation>? matches = null;
        var ordered = GetDiscoveryOrderedList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var rel = ordered[i];
            if (rel.Kind == kind && rel.From.Equals(from))
                (matches ??= new List<SpineRelation>()).Add(rel);
        }
        return matches is null ? Array.Empty<SpineRelation>() : matches;
    }

    internal IReadOnlyList<SpineRelation> GetIncomingRelationsInDiscoveryOrder(NodeReference to, RelationKind kind)
    {
        if (_incomingDiscoveryByToKind is not null || _relations.Count >= RelationIndexThreshold)
        {
            EnsureIncomingDiscoveryIndex();
            return _incomingDiscoveryByToKind!.TryGetValue((to, kind), out var list)
                ? list
                : Array.Empty<SpineRelation>();
        }

        List<SpineRelation>? matches = null;
        var ordered = GetDiscoveryOrderedList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var rel = ordered[i];
            if (rel.Kind == kind && rel.To.Equals(to))
                (matches ??= new List<SpineRelation>()).Add(rel);
        }
        return matches is null ? Array.Empty<SpineRelation>() : matches;
    }

    internal bool HasOutgoingRelation(NodeReference from, RelationKind kind)
    {
        if (_outgoingDiscoveryByFromKind is not null || _relations.Count >= RelationIndexThreshold)
        {
            EnsureOutgoingDiscoveryIndex();
            return _outgoingDiscoveryByFromKind!.TryGetValue((from, kind), out var list) && list.Count > 0;
        }

        foreach (var rel in _relations.Values)
        {
            if (rel.Kind == kind && rel.From.Equals(from))
                return true;
        }

        return false;
    }

    internal int CountOutgoingRelations(NodeReference from, RelationKind kind)
    {
        if (_outgoingDiscoveryByFromKind is not null || _relations.Count >= RelationIndexThreshold)
        {
            EnsureOutgoingDiscoveryIndex();
            return _outgoingDiscoveryByFromKind!.TryGetValue((from, kind), out var list) ? list.Count : 0;
        }

        var count = 0;
        foreach (var rel in _relations.Values)
        {
            if (rel.Kind == kind && rel.From.Equals(from))
                count++;
        }

        return count;
    }

    internal bool HasIncomingRelation(NodeReference to, RelationKind kind)
    {
        if (_incomingDiscoveryByToKind is not null || _relations.Count >= RelationIndexThreshold)
        {
            EnsureIncomingDiscoveryIndex();
            return _incomingDiscoveryByToKind!.TryGetValue((to, kind), out var list) && list.Count > 0;
        }

        foreach (var rel in _relations.Values)
        {
            if (rel.Kind == kind && rel.To.Equals(to))
                return true;
        }

        return false;
    }

    internal void CollectOutgoingRelationsUnordered(
        NodeReference from,
        RelationKind kind,
        List<SpineRelation> destination)
    {
        destination.Clear();
        if (_outgoingDiscoveryByFromKind is not null || _relations.Count >= RelationIndexThreshold)
        {
            EnsureOutgoingDiscoveryIndex();
            if (_outgoingDiscoveryByFromKind!.TryGetValue((from, kind), out var list))
                destination.AddRange(list);
            return;
        }

        foreach (var rel in _relations.Values)
        {
            if (rel.Kind == kind && rel.From.Equals(from))
                destination.Add(rel);
        }
    }

    public void Clear()
    {
        _projects.Clear();
        _components.Clear();
        _endpoints.Clear();
        _relations.Clear();
        _discoveryOrderList.Clear();
        _discoveryOrderDirty = false;
        _explicitFlowKeys.Clear();
        _promotableRefNames.Clear();
        _syntheticProjects.Clear();
        _nextDiscoverySequence = 0;
        Unproven.Clear();
        _sortedProjects = null;
        _sortedLayers = null;
        _sortedComponents = null;
        _sortedEndpoints = null;
        _projectsVersion++;
        _componentsVersion++;
        _endpointsVersion++;
        InvalidateRelationCaches();
    }

    internal bool IsExplicitFlowKey(RelationKey key) => _explicitFlowKeys.Contains(key);

    internal void MarkExplicitFlowKey(RelationKey key)
    {
        if (_explicitFlowKeys.Add(key))
            InvalidateFlowQueryCaches();
    }

    internal void UnmarkExplicitFlowKey(RelationKey key)
    {
        if (_explicitFlowKeys.Remove(key))
            InvalidateFlowQueryCaches();
    }

    internal void ClearExplicitFlowKeys()
    {
        if (_explicitFlowKeys.Count > 0)
        {
            _explicitFlowKeys.Clear();
            InvalidateFlowQueryCaches();
        }
    }

    internal int NextEndpointOrdinal(ProjectId project, string siteKey)
    {
        var count = 0;
        foreach (var existing in _endpoints.Values)
        {
            if (existing.Project.Equals(project) &&
                string.Equals(existing.Id.SiteKey, siteKey, StringComparison.Ordinal))
            {
                count++;
            }
        }
        return count;
    }

    internal bool RemoveProject(ProjectId id)
    {
        if (!_projects.Remove(id))
            return false;

        _sortedProjects = null;
        _sortedLayers = null;
        _projectsVersion++;
        InvalidateQueryCaches();
        return true;
    }

    internal bool RemoveComponentRole(ComponentId id, ComponentRole role)
    {
        if (!_components.TryGetValue(id, out var existing))
            return false;
        if (!existing.RemoveRole(role))
            return false;

        if (existing.RoleCount == 0)
            _components.Remove(id);

        _sortedComponents = null;
        _componentsVersion++;
        InvalidateQueryCaches();
        return true;
    }

    internal bool RemoveEndpoint(EndpointId id)
    {
        if (!_endpoints.Remove(id))
            return false;

        _sortedEndpoints = null;
        _endpointsVersion++;
        InvalidateQueryCaches();
        return true;
    }

    internal bool RemoveRelation(RelationKey key)
    {
        if (!_relations.Remove(key))
            return false;

        _discoveryOrderDirty = true;
        _explicitFlowKeys.Remove(key);
        InvalidateRelationCaches();
        return true;
    }

    internal int RemoveRelations(Func<SpineRelation, bool> predicate)
    {
        var toRemove = _relations.Values.Where(predicate).Select(r => r.Key).ToList();
        if (toRemove.Count == 0)
            return 0;

        foreach (var key in toRemove)
        {
            _relations.Remove(key);
            _explicitFlowKeys.Remove(key);
        }

        _discoveryOrderDirty = true;
        InvalidateRelationCaches();
        return toRemove.Count;
    }

    internal void RehomeSyntheticProject(ProjectId syntheticProjectId, ProjectId realProjectId)
    {
        if (syntheticProjectId.Equals(realProjectId) || !_syntheticProjects.Remove(syntheticProjectId))
            return;

        foreach (var kv in _components.Where(p => p.Key.Project.Equals(syntheticProjectId)).ToList())
        {
            _components.Remove(kv.Key);
            var newSymbol = RehomeSymbol(kv.Key.Symbol, realProjectId);
            var newId = ComponentId.FromSymbol(newSymbol);
            foreach (var roleEvidence in kv.Value.Roles)
            {
                foreach (var occ in roleEvidence.Evidence)
                    AddOrMergeComponent(newId, kv.Value.Location, roleEvidence.Role, RehomeOccurrence(occ, syntheticProjectId, realProjectId));
            }
            _sortedComponents = null;
            _componentsVersion++;
            InvalidateQueryCaches();
        }

        foreach (var kv in _endpoints.Where(p => p.Key.Project.Equals(syntheticProjectId)).ToList())
        {
            _endpoints.Remove(kv.Key);
            var ordinal = NextEndpointOrdinal(realProjectId, kv.Key.SiteKey);
            var newId = kv.Key.MappingSite is not null
                ? EndpointId.Create(realProjectId, kv.Key.MappingSite, ordinal)
                : EndpointId.FromSyntheticSite(realProjectId, kv.Key.SiteKey, ordinal);
            var rehomed = new EndpointNode(
                newId,
                kv.Value.Name,
                kv.Value.HttpMethod,
                kv.Value.Route,
                kv.Value.VerbStrength,
                kv.Value.RouteStrength,
                kv.Value.Evidence.Select(o => RehomeOccurrence(o, syntheticProjectId, realProjectId)).ToArray());
            _endpoints[newId] = rehomed;
            _sortedEndpoints = null;
            _endpointsVersion++;
            InvalidateQueryCaches();
        }

        RewriteRelations(r =>
        {
            if (r.ProjectId?.Equals(syntheticProjectId) != true)
                return null;
            return RehomeNodeReference(r, syntheticProjectId, realProjectId);
        }, syntheticProjectId, realProjectId);
    }

    private void PromoteReferencesToComponent(ComponentId componentId)
    {
        var fullName = componentId.Symbol.FullName;
        var qualifiedName = componentId.Symbol.QualifiedName;
        if (!_promotableRefNames.Contains(fullName) && !_promotableRefNames.Contains(qualifiedName))
            return;

        var target = NodeReference.ForComponent(componentId);
        RewriteRelations(r =>
        {
            if (r.Kind is NodeReferenceKind.External or NodeReferenceKind.Type &&
                (string.Equals(r.Name, fullName, StringComparison.Ordinal) ||
                 string.Equals(r.Name, qualifiedName, StringComparison.Ordinal)))
            {
                return target;
            }
            return null;
        }, null, null);
    }

    private void RewriteRelations(
        Func<NodeReference, NodeReference?> mapRef,
        ProjectId? oldProj,
        ProjectId? newProj)
    {
        var updated = new List<(RelationKey OldKey, SpineRelation Relation, bool IsExplicit)>();
        foreach (var relation in _relations.Values)
        {
            var newFrom = mapRef(relation.From) ?? relation.From;
            var newTo = mapRef(relation.To) ?? relation.To;
            var changed = !ReferenceEquals(newFrom, relation.From) || !ReferenceEquals(newTo, relation.To);
            var occChanged = changed;

            if (!occChanged)
            {
                foreach (var occ in relation.Occurrences)
                {
                    if (occ.Via.Any(v => mapRef(v) is not null))
                    {
                        occChanged = true;
                        break;
                    }
                }
            }

            if (!changed && !occChanged)
                continue;

            var newOccurrences = new List<EvidenceOccurrence>(relation.OccurrenceCount);
            foreach (var occ in relation.Occurrences)
            {
                var reOcc = oldProj is not null && newProj is not null
                    ? RehomeOccurrence(occ, oldProj, newProj)
                    : RewriteOccurrenceVia(occ, mapRef);
                newOccurrences.Add(reOcc);
            }

            var rebuilt = new SpineRelation(relation.Kind, newFrom, newTo, newOccurrences[0], relation.Method)
            {
                DiscoverySequence = relation.DiscoverySequence,
            };
            for (var i = 1; i < newOccurrences.Count; i++)
                rebuilt.AddOccurrence(newOccurrences[i]);

            updated.Add((relation.Key, rebuilt, _explicitFlowKeys.Contains(relation.Key)));
        }

        if (updated.Count == 0)
            return;

        foreach (var (oldKey, rebuilt, isExplicit) in updated)
        {
            _relations.Remove(oldKey);
            _explicitFlowKeys.Remove(oldKey);

            if (_relations.TryGetValue(rebuilt.Key, out var existing))
            {
                foreach (var occ in rebuilt.Occurrences)
                    existing.AddOccurrence(occ);
            }
            else
            {
                _relations[rebuilt.Key] = rebuilt;
            }

            if (isExplicit)
                _explicitFlowKeys.Add(rebuilt.Key);
        }

        _discoveryOrderDirty = true;
        InvalidateRelationCaches();
    }

    internal FlowEdge[] GetOrBuildFlowEdges(bool flowAnalyzed, Func<FlowEdge[]> build)
    {
        if (flowAnalyzed)
            return _cachedFlowEdgesTrue ??= build();
        return _cachedFlowEdgesFalse ??= build();
    }

    internal ResolvedEdge[] GetOrBuildResolvedEdges(Func<ResolvedEdge[]> build)
        => _cachedResolvedEdges ??= build();

    internal ServiceCallEdge[] GetOrBuildServiceCallEdges(Func<ServiceCallEdge[]> build)
        => _cachedServiceCallEdges ??= build();

    internal DispatchCallSite[] GetOrBuildDispatchCallSites(Func<DispatchCallSite[]> build)
        => _cachedDispatchCallSites ??= build();

    private void InvalidateRelationCaches()
    {
        _sortedRelations = null;
        _relationsInDiscoveryOrder = null;
        _outgoingDiscoveryByFromKind = null;
        _incomingDiscoveryByToKind = null;
        _relationsByKind = null;
        _outgoingByFrom = null;
        _outgoingByFromKind = null;
        _incomingByTo = null;
        _incomingByToKind = null;
        InvalidateQueryCaches();
    }

    private void EnsureOutgoingDiscoveryIndex()
    {
        if (_outgoingDiscoveryByFromKind is not null)
            return;

        var ordered = GetDiscoveryOrderedList();
        var map = new Dictionary<(NodeReference, RelationKind), List<SpineRelation>>();
        for (var i = 0; i < ordered.Count; i++)
        {
            var rel = ordered[i];
            var key = (rel.From, rel.Kind);
            if (!map.TryGetValue(key, out var list))
                map[key] = list = new List<SpineRelation>(2);
            list.Add(rel);
        }
        _outgoingDiscoveryByFromKind = map;
    }

    private void EnsureIncomingDiscoveryIndex()
    {
        if (_incomingDiscoveryByToKind is not null)
            return;

        var ordered = GetDiscoveryOrderedList();
        var map = new Dictionary<(NodeReference, RelationKind), List<SpineRelation>>();
        for (var i = 0; i < ordered.Count; i++)
        {
            var rel = ordered[i];
            var key = (rel.To, rel.Kind);
            if (!map.TryGetValue(key, out var list))
                map[key] = list = new List<SpineRelation>(2);
            list.Add(rel);
        }
        _incomingDiscoveryByToKind = map;
    }

    private void EnsureRelationIndexes()
    {
        if (_relationsByKind is not null)
            return;

        var byKind = new Dictionary<RelationKind, List<SpineRelation>>();
        var outgoing = new Dictionary<NodeReference, List<SpineRelation>>();
        var outgoingKind = new Dictionary<(NodeReference, RelationKind), List<SpineRelation>>();
        var incoming = new Dictionary<NodeReference, List<SpineRelation>>();
        var incomingKind = new Dictionary<(NodeReference, RelationKind), List<SpineRelation>>();

        foreach (var rel in Relations)
        {
            if (!byKind.TryGetValue(rel.Kind, out var kindList))
                byKind[rel.Kind] = kindList = new List<SpineRelation>();
            kindList.Add(rel);

            if (!outgoing.TryGetValue(rel.From, out var outList))
                outgoing[rel.From] = outList = new List<SpineRelation>();
            outList.Add(rel);

            var okKey = (rel.From, rel.Kind);
            if (!outgoingKind.TryGetValue(okKey, out var okList))
                outgoingKind[okKey] = okList = new List<SpineRelation>();
            okList.Add(rel);

            if (!incoming.TryGetValue(rel.To, out var inList))
                incoming[rel.To] = inList = new List<SpineRelation>();
            inList.Add(rel);

            var ikKey = (rel.To, rel.Kind);
            if (!incomingKind.TryGetValue(ikKey, out var ikList))
                incomingKind[ikKey] = ikList = new List<SpineRelation>();
            ikList.Add(rel);
        }

        _relationsByKind = byKind;
        _outgoingByFrom = outgoing;
        _outgoingByFromKind = outgoingKind;
        _incomingByTo = incoming;
        _incomingByToKind = incomingKind;
    }

    private void InvalidateQueryCaches()
    {
        _cachedFlowEdgesFalse = null;
        _cachedFlowEdgesTrue = null;
        _cachedResolvedEdges = null;
        _cachedServiceCallEdges = null;
        _cachedDispatchCallSites = null;
        _version++;
    }

    private void InvalidateFlowQueryCaches()
    {
        _cachedFlowEdgesFalse = null;
        _cachedFlowEdgesTrue = null;
        _version++;
    }

    private void TrackRef(NodeReference reference)
    {
        if (reference.ProjectId is not null)
            TrackSyntheticProject(reference.ProjectId);

        if (reference.Kind is NodeReferenceKind.External or NodeReferenceKind.Type)
            _promotableRefNames.Add(reference.Name);
    }

    private void TrackSyntheticProject(ProjectId project)
    {
        if (project.Value.StartsWith("__synthetic__/", StringComparison.Ordinal))
            _syntheticProjects.Add(project);
    }

    private static SymbolId RehomeSymbol(SymbolId symbol, ProjectId newProject)
        => SymbolId.Create(newProject, symbol.Namespace, symbol.ContainingTypes, symbol.Name, symbol.GenericArity, symbol.TypeArguments);

    private static NodeReference RehomeNodeReference(NodeReference r, ProjectId oldProject, ProjectId newProject)
    {
        if (r.Kind == NodeReferenceKind.Project)
            return NodeReference.ForProject(newProject);
        if (r.Kind == NodeReferenceKind.Component && r.ComponentId is not null)
            return NodeReference.ForComponent(ComponentId.FromSymbol(RehomeSymbol(r.ComponentId.Symbol, newProject)));
        if (r.Kind == NodeReferenceKind.Interface && r.SymbolId is not null)
            return NodeReference.ForInterface(RehomeSymbol(r.SymbolId, newProject));
        if (r.Kind == NodeReferenceKind.Method && r.SymbolId is not null && r.MemberName is not null)
            return NodeReference.ForMethod(RehomeSymbol(r.SymbolId, newProject), r.MemberName);
        if (r.Kind == NodeReferenceKind.Member && r.SymbolId is not null && r.MemberName is not null)
            return NodeReference.ForMember(RehomeSymbol(r.SymbolId, newProject), r.MemberName);
        if (r.Kind == NodeReferenceKind.Type && r.SymbolId is not null)
            return NodeReference.ForType(RehomeSymbol(r.SymbolId, newProject));
        if (r.Kind == NodeReferenceKind.Endpoint && r.EndpointId is not null)
        {
            var ep = r.EndpointId.MappingSite is not null
                ? EndpointId.Create(newProject, r.EndpointId.MappingSite, r.EndpointId.Ordinal)
                : EndpointId.FromSyntheticSite(newProject, r.EndpointId.SiteKey, r.EndpointId.Ordinal);
            return NodeReference.ForEndpoint(ep);
        }
        return r;
    }

    private static EvidenceOccurrence RehomeOccurrence(EvidenceOccurrence occ, ProjectId oldProject, ProjectId newProject)
    {
        if (occ.Via.Count == 0)
            return occ;

        var changed = false;
        var newVia = new NodeReference[occ.Via.Count];
        for (var i = 0; i < occ.Via.Count; i++)
        {
            if (occ.Via[i].ProjectId?.Equals(oldProject) == true)
            {
                newVia[i] = RehomeNodeReference(occ.Via[i], oldProject, newProject);
                changed = true;
            }
            else
            {
                newVia[i] = occ.Via[i];
            }
        }

        return changed
            ? new EvidenceOccurrence(
                occ.Rule, occ.Origin, occ.Location, occ.Strength, newVia,
                occ.LocationOmittedReason, occ.ResolutionSemantics, occ.ScopeProjectName, occ.IsTestProject, occ.Detail)
            : occ;
    }

    private static EvidenceOccurrence RewriteOccurrenceVia(EvidenceOccurrence occ, Func<NodeReference, NodeReference?> mapRef)
    {
        if (occ.Via.Count == 0)
            return occ;

        var changed = false;
        var newVia = new NodeReference[occ.Via.Count];
        for (var i = 0; i < occ.Via.Count; i++)
        {
            var mapped = mapRef(occ.Via[i]);
            if (mapped is not null)
            {
                newVia[i] = mapped;
                changed = true;
            }
            else
            {
                newVia[i] = occ.Via[i];
            }
        }

        return changed
            ? new EvidenceOccurrence(
                occ.Rule, occ.Origin, occ.Location, occ.Strength, newVia,
                occ.LocationOmittedReason, occ.ResolutionSemantics, occ.ScopeProjectName, occ.IsTestProject, occ.Detail)
            : occ;
    }
}
