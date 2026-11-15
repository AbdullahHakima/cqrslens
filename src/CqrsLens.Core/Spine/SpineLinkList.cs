namespace CqrsLens;

/// <summary>
/// Live projection over <see cref="ArchitectureSpine"/> relations for shape linking lists
/// (<see cref="CqrsRequest.Handlers"/>, <see cref="CqrsRequest.Validators"/>,
/// <see cref="HandlerInfo.TouchedEntities"/>, <see cref="ProjectInfo.ReferenceNames"/>, etc.).
/// Buffers writes while the owning shape is detached, then flushes into the spine on attachment.
/// </summary>
public sealed class SpineLinkList : SpineProjectedCollection<string>
{
    private readonly SpineLinkKind _kind;
    private List<string>? _pending = new();
    private AnalysisResult? _result;
    private Func<NodeReference?>? _ownerRefFactory;
    private Comparison<string>? _sortComparison;

    internal SpineLinkList(SpineLinkKind kind, bool sortedCaseInsensitive = false)
    {
        _kind = kind;
        if (sortedCaseInsensitive)
            _sortComparison = StringComparer.OrdinalIgnoreCase.Compare;
    }

    internal bool IsAttached => _result is not null;

    internal void Attach(AnalysisResult result, Func<NodeReference?> ownerRefFactory)
    {
        _result = result;
        _ownerRefFactory = ownerRefFactory;

        if (_pending is not null && _pending.Count > 0)
        {
            var toFlush = _pending;
            _pending = null;
            foreach (var item in toFlush)
                AddToSpine(item);
        }
        else
        {
            _pending = null;
        }

        InvalidateCache();
    }

    protected override long CacheStamp => _result is null ? -1 : _result.Spine.Version;

    protected override IReadOnlyList<string> Project()
    {
        if (_result is null)
            return ApplySort(_pending!);

        var owner = _ownerRefFactory?.Invoke();
        if (owner is null)
            return Array.Empty<string>();

        var spine = _result.Spine;
        var resolver = _result.IdentityResolver;
        var items = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        switch (_kind)
        {
            case SpineLinkKind.ProjectReferences:
                foreach (var name in SpineQueries.ProjectReferences(spine, resolver, owner))
                    AddUnique(items, seen, name);
                break;

            case SpineLinkKind.RequestHandlers:
            {
                var rels = spine.GetOutgoingRelationsInDiscoveryOrder(owner, RelationKind.Serves);
                for (var i = 0; i < rels.Count; i++)
                {
                    var rel = rels[i];
                    AddUnique(items, seen, rel.To.SymbolId?.FullName ?? rel.To.Name);
                }
                break;
            }

            case SpineLinkKind.RequestValidators:
            {
                var rels = spine.GetIncomingRelationsInDiscoveryOrder(owner, RelationKind.Validates);
                for (var i = 0; i < rels.Count; i++)
                {
                    var rel = rels[i];
                    AddUnique(items, seen, rel.From.SymbolId?.FullName ?? rel.From.Name);
                }
                break;
            }

            case SpineLinkKind.RequestEntrypoints:
            {
                var rels = spine.GetIncomingRelationsInDiscoveryOrder(owner, RelationKind.Dispatches);
                for (var i = 0; i < rels.Count; i++)
                {
                    var rel = rels[i];
                    if (rel.From.Kind == NodeReferenceKind.Endpoint && rel.From.EndpointId is not null)
                    {
                        if (spine.TryGetEndpoint(rel.From.EndpointId, out var ep))
                        {
                            var label = ep!.IsInferred ? $"{ep.DisplayLabel} _(inferred)_" : ep.DisplayLabel;
                            AddUnique(items, seen, label);
                        }
                    }
                    else if (rel.From.Kind == NodeReferenceKind.Unresolved &&
                             string.Equals(rel.From.Reason, "Manual entrypoint label", StringComparison.Ordinal))
                    {
                        AddUnique(items, seen, rel.From.Name);
                    }
                }
                break;
            }

            case SpineLinkKind.RequestTouchedEntities:
                CollectRequestRollup(
                    spine,
                    owner,
                    RelationKind.TouchesData,
                    r => true,
                    r => resolver.FormatTouchesDataTarget(r.To),
                    items,
                    seen);
                break;

            case SpineLinkKind.RequestPublishedNotifications:
                CollectRequestRollup(
                    spine,
                    owner,
                    RelationKind.Publishes,
                    IsDeepSliceOrManualOccurrence,
                    r => r.To.SymbolId?.FullName ?? r.To.Name,
                    items,
                    seen);
                break;

            case SpineLinkKind.RequestDispatchedRequests:
                CollectRequestRollup(
                    spine,
                    owner,
                    RelationKind.Dispatches,
                    IsDeepSliceOrManualOccurrence,
                    r => r.To.SymbolId?.FullName ?? r.To.Name,
                    items,
                    seen);
                break;

            case SpineLinkKind.NotificationHandlers:
            {
                var rels = spine.GetOutgoingRelationsInDiscoveryOrder(owner, RelationKind.HandlesNotification);
                for (var i = 0; i < rels.Count; i++)
                {
                    var rel = rels[i];
                    AddUnique(items, seen, rel.To.SymbolId?.FullName ?? rel.To.Name);
                }
                break;
            }

            case SpineLinkKind.HandlerTouchedEntities:
            {
                var rels = spine.GetOutgoingRelationsInDiscoveryOrder(owner, RelationKind.TouchesData);
                for (var i = 0; i < rels.Count; i++)
                    AddUnique(items, seen, resolver.FormatTouchesDataTarget(rels[i].To));
                break;
            }

            case SpineLinkKind.HandlerPublishedNotifications:
            {
                var rels = spine.GetOutgoingRelationsInDiscoveryOrder(owner, RelationKind.Publishes);
                for (var i = 0; i < rels.Count; i++)
                {
                    var rel = rels[i];
                    if (IsDeepSliceOrManualOccurrence(rel))
                        AddUnique(items, seen, rel.To.SymbolId?.FullName ?? rel.To.Name);
                }
                break;
            }

            case SpineLinkKind.HandlerDispatchedRequests:
            {
                var rels = spine.GetOutgoingRelationsInDiscoveryOrder(owner, RelationKind.Dispatches);
                for (var i = 0; i < rels.Count; i++)
                {
                    var rel = rels[i];
                    if (IsDeepSliceOrManualOccurrence(rel))
                        AddUnique(items, seen, rel.To.SymbolId?.FullName ?? rel.To.Name);
                }
                break;
            }
        }

        return ApplySort(items);
    }

    protected override void AddCore(string item)
    {
        if (_result is null)
        {
            _pending!.Add(item);
            return;
        }

        AddToSpine(item);
    }

    protected override bool RemoveCore(string item)
    {
        if (_result is null)
            return _pending!.Remove(item);

        var owner = _ownerRefFactory?.Invoke();
        if (owner is null)
            return false;

        var spine = _result.Spine;
        var resolver = _result.IdentityResolver;
        return _kind switch
        {
            SpineLinkKind.ProjectReferences => spine.RemoveRelations(r =>
                r.Kind == RelationKind.References && r.From.Equals(owner) &&
                string.Equals(
                    r.To.ProjectId is not null ? resolver.ProjectNameFor(r.To.ProjectId) ?? StripSyntheticPrefix(r.To.ProjectId.Value) : r.To.Name,
                    item,
                    StringComparison.Ordinal)) > 0,
            SpineLinkKind.RequestHandlers => spine.RemoveRelations(r =>
                r.Kind == RelationKind.Serves && r.From.Equals(owner) &&
                string.Equals(r.To.SymbolId?.FullName ?? r.To.Name, item, StringComparison.Ordinal)) > 0,
            SpineLinkKind.RequestValidators => spine.RemoveRelations(r =>
                r.Kind == RelationKind.Validates && r.To.Equals(owner) &&
                string.Equals(r.From.SymbolId?.FullName ?? r.From.Name, item, StringComparison.Ordinal)) > 0,
            SpineLinkKind.NotificationHandlers => spine.RemoveRelations(r =>
                r.Kind == RelationKind.HandlesNotification && r.From.Equals(owner) &&
                string.Equals(r.To.SymbolId?.FullName ?? r.To.Name, item, StringComparison.Ordinal)) > 0,
            SpineLinkKind.HandlerTouchedEntities or SpineLinkKind.RequestTouchedEntities => spine.RemoveRelations(r =>
                r.Kind == RelationKind.TouchesData && r.From.Equals(owner) &&
                string.Equals(resolver.FormatTouchesDataTarget(r.To), item, StringComparison.Ordinal)) > 0,
            SpineLinkKind.HandlerPublishedNotifications or SpineLinkKind.RequestPublishedNotifications => spine.RemoveRelations(r =>
                r.Kind == RelationKind.Publishes && r.From.Equals(owner) &&
                string.Equals(r.To.SymbolId?.FullName ?? r.To.Name, item, StringComparison.Ordinal)) > 0,
            SpineLinkKind.HandlerDispatchedRequests or SpineLinkKind.RequestDispatchedRequests => spine.RemoveRelations(r =>
                r.Kind == RelationKind.Dispatches && r.From.Equals(owner) &&
                string.Equals(r.To.SymbolId?.FullName ?? r.To.Name, item, StringComparison.Ordinal)) > 0,
            _ => false,
        };
    }

    protected override void ClearCore()
    {
        if (_result is null)
        {
            _pending!.Clear();
            return;
        }

        var owner = _ownerRefFactory?.Invoke();
        if (owner is null)
            return;

        var spine = _result.Spine;
        switch (_kind)
        {
            case SpineLinkKind.ProjectReferences:
                spine.RemoveRelations(r => r.Kind == RelationKind.References && r.From.Equals(owner));
                break;
            case SpineLinkKind.RequestHandlers:
                spine.RemoveRelations(r => r.Kind == RelationKind.Serves && r.From.Equals(owner));
                break;
            case SpineLinkKind.RequestValidators:
                spine.RemoveRelations(r => r.Kind == RelationKind.Validates && r.To.Equals(owner));
                break;
            case SpineLinkKind.NotificationHandlers:
                spine.RemoveRelations(r => r.Kind == RelationKind.HandlesNotification && r.From.Equals(owner));
                break;
            case SpineLinkKind.HandlerTouchedEntities or SpineLinkKind.RequestTouchedEntities:
                spine.RemoveRelations(r => r.Kind == RelationKind.TouchesData && r.From.Equals(owner));
                break;
            case SpineLinkKind.HandlerPublishedNotifications or SpineLinkKind.RequestPublishedNotifications:
                spine.RemoveRelations(r => r.Kind == RelationKind.Publishes && r.From.Equals(owner) && IsDeepSliceOrManualOccurrence(r));
                break;
            case SpineLinkKind.HandlerDispatchedRequests or SpineLinkKind.RequestDispatchedRequests:
                spine.RemoveRelations(r => r.Kind == RelationKind.Dispatches && r.From.Equals(owner) && IsDeepSliceOrManualOccurrence(r));
                break;
        }
    }

    protected override void SortCore(Comparison<string>? comparison)
    {
        _sortComparison = comparison ?? StringComparer.Ordinal.Compare;
        if (_pending is not null)
            _pending.Sort(_sortComparison);
        InvalidateCache();
    }

    private void AddToSpine(string item)
    {
        if (_result is null)
            return;

        var owner = _ownerRefFactory?.Invoke();
        if (owner is null)
            return;

        var spine = _result.Spine;
        var resolver = _result.IdentityResolver;
        var ownerProject = owner.ProjectId is not null ? resolver.ProjectNameFor(owner.ProjectId) : null;

        switch (_kind)
        {
            case SpineLinkKind.ProjectReferences:
            {
                var targetProj = resolver.ResolveProjectId(item);
                spine.AddOrMergeRelation(
                    RelationKind.References,
                    owner,
                    NodeReference.ForProject(targetProj),
                    SpineIdentityResolver.SyntheticOccurrence(
                        EvidenceRule.ProjectReference,
                        EvidenceOrigin.ProjectFile,
                        null,
                        EvidenceStrength.Proven,
                        scopeProjectName: ownerProject));
                break;
            }

            case SpineLinkKind.RequestHandlers:
            {
                var handlerRef = resolver.ResolveComponentOrExternalRef(item, ComponentRole.Handler, ownerProject);
                var handlerComp = handlerRef.ComponentId is not null && spine.TryGetComponent(handlerRef.ComponentId, out var c) ? c : null;
                spine.AddOrMergeRelation(
                    RelationKind.Serves,
                    owner,
                    handlerRef,
                    SpineIdentityResolver.SyntheticOccurrence(
                        EvidenceRule.HandlerMarker,
                        EvidenceOrigin.Declaration,
                        handlerComp?.Location,
                        EvidenceStrength.Proven,
                        scopeProjectName: ownerProject));
                break;
            }

            case SpineLinkKind.RequestValidators:
            {
                var valRef = resolver.ResolveComponentOrExternalRef(item, ComponentRole.Validator, ownerProject);
                var valComp = valRef.ComponentId is not null && spine.TryGetComponent(valRef.ComponentId, out var c) ? c : null;
                spine.AddOrMergeRelation(
                    RelationKind.Validates,
                    valRef,
                    owner,
                    SpineIdentityResolver.SyntheticOccurrence(
                        EvidenceRule.ValidatorMarker,
                        EvidenceOrigin.Declaration,
                        valComp?.Location,
                        EvidenceStrength.Proven,
                        scopeProjectName: ownerProject));
                break;
            }

            case SpineLinkKind.RequestEntrypoints:
                spine.AddOrMergeRelation(
                    RelationKind.Dispatches,
                    NodeReference.ForUnresolved(item, "Manual entrypoint label"),
                    owner,
                    new EvidenceOccurrence(
                        EvidenceRule.EndpointDispatch,
                        EvidenceOrigin.MappingOrAttribute,
                        null,
                        EvidenceStrength.Inferred,
                        locationOmittedReason: "Manual entrypoint label",
                        scopeProjectName: ownerProject));
                break;

            case SpineLinkKind.HandlerTouchedEntities:
            case SpineLinkKind.RequestTouchedEntities:
            {
                var target = resolver.ResolveTouchesDataTarget(item, ownerProject);
                var ownerComp = owner.ComponentId is not null && spine.TryGetComponent(owner.ComponentId, out var c) ? c : null;
                spine.AddOrMergeRelation(
                    RelationKind.TouchesData,
                    owner,
                    target,
                    SpineIdentityResolver.SyntheticOccurrence(
                        EvidenceRule.DeepSliceDataAccess,
                        EvidenceOrigin.CallSite,
                        ownerComp?.Location,
                        EvidenceStrength.Inferred,
                        scopeProjectName: ownerProject));
                break;
            }

            case SpineLinkKind.HandlerPublishedNotifications:
            case SpineLinkKind.RequestPublishedNotifications:
            {
                var notifRef = resolver.ResolveComponentOrExternalRef(item, ComponentRole.Notification, ownerProject);
                var ownerComp = owner.ComponentId is not null && spine.TryGetComponent(owner.ComponentId, out var c) ? c : null;
                spine.AddOrMergeRelation(
                    RelationKind.Publishes,
                    owner,
                    notifRef,
                    SpineIdentityResolver.SyntheticOccurrence(
                        EvidenceRule.DeepSliceDataAccess,
                        EvidenceOrigin.CallSite,
                        ownerComp?.Location,
                        EvidenceStrength.Inferred,
                        scopeProjectName: ownerProject));
                break;
            }

            case SpineLinkKind.HandlerDispatchedRequests:
            case SpineLinkKind.RequestDispatchedRequests:
            {
                var reqRef = resolver.ResolveComponentOrExternalRef(item, ComponentRole.Request, ownerProject);
                var ownerComp = owner.ComponentId is not null && spine.TryGetComponent(owner.ComponentId, out var c) ? c : null;
                spine.AddOrMergeRelation(
                    RelationKind.Dispatches,
                    owner,
                    reqRef,
                    SpineIdentityResolver.SyntheticOccurrence(
                        EvidenceRule.DeepSliceDataAccess,
                        EvidenceOrigin.CallSite,
                        ownerComp?.Location,
                        EvidenceStrength.Inferred,
                        scopeProjectName: ownerProject));
                break;
            }

            case SpineLinkKind.NotificationHandlers:
            {
                var nhRef = resolver.ResolveComponentOrExternalRef(item, ComponentRole.NotificationHandler, ownerProject);
                var nhComp = nhRef.ComponentId is not null && spine.TryGetComponent(nhRef.ComponentId, out var c) ? c : null;
                spine.AddOrMergeRelation(
                    RelationKind.HandlesNotification,
                    owner,
                    nhRef,
                    SpineIdentityResolver.SyntheticOccurrence(
                        EvidenceRule.NotificationHandlerMarker,
                        EvidenceOrigin.Declaration,
                        nhComp?.Location,
                        EvidenceStrength.Proven,
                        scopeProjectName: ownerProject));
                break;
            }
        }
    }

    private void CollectRequestRollup(
        ArchitectureSpine spine,
        NodeReference requestRef,
        RelationKind relationKind,
        Func<SpineRelation, bool> predicate,
        Func<SpineRelation, string> format,
        List<string> items,
        HashSet<string> seen)
    {
        var directRels = spine.GetOutgoingRelationsInDiscoveryOrder(requestRef, relationKind);
        for (var i = 0; i < directRels.Count; i++)
        {
            var direct = directRels[i];
            if (predicate(direct))
                AddUnique(items, seen, format(direct));
        }

        // Preserve HandlerEffectRollup order: iterate request.Handlers in its current order,
        // then append each handler's outgoing relations in discovery order.
        var servesRels = spine.GetOutgoingRelationsInDiscoveryOrder(requestRef, RelationKind.Serves);
        if (servesRels.Count == 0)
            return;

        IReadOnlyList<SpineRelation> orderedServes = servesRels;
        if (servesRels.Count > 1)
        {
            var sorted = new List<SpineRelation>(servesRels);
            sorted.Sort((a, b) =>
            {
                var left = a.To.SymbolId?.FullName ?? a.To.Name;
                var right = b.To.SymbolId?.FullName ?? b.To.Name;
                return _sortComparison is not null
                    ? _sortComparison(left, right)
                    : StringComparer.OrdinalIgnoreCase.Compare(left, right);
            });
            orderedServes = sorted;
        }

        for (var s = 0; s < orderedServes.Count; s++)
        {
            var rels = spine.GetOutgoingRelationsInDiscoveryOrder(orderedServes[s].To, relationKind);
            for (var i = 0; i < rels.Count; i++)
            {
                var rel = rels[i];
                if (predicate(rel))
                    AddUnique(items, seen, format(rel));
            }
        }
    }

    private static bool IsDeepSliceOrManualOccurrence(SpineRelation relation)
    {
        foreach (var occ in relation.Occurrences)
        {
            if (occ.Rule == EvidenceRule.DeepSliceDataAccess)
                return true;
        }
        return false;
    }

    private IReadOnlyList<string> ApplySort(List<string> items)
    {
        if (_sortComparison is not null && items.Count > 1)
            items.Sort(_sortComparison);
        return items;
    }

    private static void AddUnique(List<string> items, HashSet<string> seen, string value)
    {
        if (seen.Add(value))
            items.Add(value);
    }

    private static string StripSyntheticPrefix(string value)
    {
        const string prefix = "__synthetic__/";
        return value.StartsWith(prefix, StringComparison.Ordinal) ? value[prefix.Length..] : value;
    }
}
