namespace CqrsLens;

/// <summary>
/// Live projection of <see cref="AnalysisResult.FlowEdges"/> from <see cref="ArchitectureSpine"/>.
/// </summary>
public sealed class SpineFlowEdgeList : SpineProjectedCollection<FlowEdge>
{
    private readonly AnalysisResult _result;

    internal SpineFlowEdgeList(AnalysisResult result)
    {
        _result = result;
    }

    protected override long CacheStamp
        => ((long)_result.Spine.Version << 1) | (_result.FlowAnalyzed ? 1L : 0L);

    protected override IReadOnlyList<FlowEdge> Project()
        => SpineQueries.FlowEdges(_result);

    protected override void AddCore(FlowEdge item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var spine = _result.Spine;
        var resolver = _result.IdentityResolver;

        switch (item.Kind)
        {
            case FlowEdgeKind.EndpointToRequest:
            {
                var from = resolver.ResolveEndpointSourceRef(item.From, item.ProjectName, item.Location);
                var to = resolver.ResolveComponentOrExternalRef(item.To, ComponentRole.Request, item.ProjectName);
                var rel = spine.AddOrMergeRelation(
                    RelationKind.Dispatches,
                    from,
                    to,
                    SpineIdentityResolver.SyntheticOccurrence(
                        EvidenceRule.EndpointDispatch,
                        EvidenceOrigin.MappingOrAttribute,
                        item.Location,
                        EvidenceStrength.Proven,
                        scopeProjectName: item.ProjectName));
                spine.MarkExplicitFlowKey(rel.Key);
                break;
            }

            case FlowEdgeKind.RequestToHandler:
            {
                var from = resolver.ResolveComponentOrExternalRef(item.From, ComponentRole.Request, item.ProjectName);
                var to = resolver.ResolveComponentOrExternalRef(item.To, ComponentRole.Handler, item.ProjectName);
                var rel = spine.AddOrMergeRelation(
                    RelationKind.Serves,
                    from,
                    to,
                    SpineIdentityResolver.SyntheticOccurrence(
                        EvidenceRule.HandlerMarker,
                        EvidenceOrigin.Declaration,
                        item.Location,
                        EvidenceStrength.Proven,
                        scopeProjectName: item.ProjectName));
                spine.MarkExplicitFlowKey(rel.Key);
                break;
            }

            case FlowEdgeKind.HandlerToRequest:
            {
                var from = resolver.ResolveComponentOrExternalRef(item.From, ComponentRole.Handler, item.ProjectName);
                var to = resolver.ResolveComponentOrExternalRef(item.To, ComponentRole.Request, item.ProjectName);
                var occ = SpineIdentityResolver.SyntheticOccurrence(
                    EvidenceRule.DispatchCallSite,
                    EvidenceOrigin.CallSite,
                    item.Location,
                    EvidenceStrength.Proven,
                    scopeProjectName: item.ProjectName);
                occ.ExcludeFromDispatchCallSites = true;
                var rel = spine.AddOrMergeRelation(
                    RelationKind.Dispatches,
                    from,
                    to,
                    occ);
                spine.MarkExplicitFlowKey(rel.Key);
                break;
            }

            case FlowEdgeKind.HandlerToNotification:
            {
                var from = resolver.ResolveComponentOrExternalRef(item.From, ComponentRole.Handler, item.ProjectName);
                var to = resolver.ResolveComponentOrExternalRef(item.To, ComponentRole.Notification, item.ProjectName);
                var occ = SpineIdentityResolver.SyntheticOccurrence(
                    EvidenceRule.PublishCallSite,
                    EvidenceOrigin.CallSite,
                    item.Location,
                    EvidenceStrength.Proven,
                    scopeProjectName: item.ProjectName);
                occ.ExcludeFromDispatchCallSites = true;
                var rel = spine.AddOrMergeRelation(
                    RelationKind.Publishes,
                    from,
                    to,
                    occ);
                spine.MarkExplicitFlowKey(rel.Key);
                break;
            }

            case FlowEdgeKind.HandlerToDbContext:
            {
                var from = resolver.ResolveComponentOrExternalRef(item.From, ComponentRole.Handler, item.ProjectName);
                var to = resolver.ResolveComponentOrExternalRef(item.To, ComponentRole.DbContext, item.ProjectName);
                var rel = spine.AddOrMergeRelation(
                    RelationKind.UsesData,
                    from,
                    to,
                    SpineIdentityResolver.SyntheticOccurrence(
                        EvidenceRule.DirectDbContextReference,
                        EvidenceOrigin.Declaration,
                        item.Location,
                        EvidenceStrength.Proven,
                        scopeProjectName: item.ProjectName));
                spine.MarkExplicitFlowKey(rel.Key);
                break;
            }
        }
    }

    protected override bool RemoveCore(FlowEdge item)
    {
        if (item is null)
            return false;

        var spine = _result.Spine;
        return spine.RemoveRelations(r =>
            item.Kind switch
            {
                FlowEdgeKind.EndpointToRequest => r.Kind == RelationKind.Dispatches &&
                    r.Occurrences.Any(o => o.Rule == EvidenceRule.EndpointDispatch) &&
                    string.Equals(EndpointFromName(r, spine), item.From, StringComparison.Ordinal) &&
                    string.Equals(r.To.SymbolId?.FullName ?? r.To.Name, item.To, StringComparison.Ordinal),
                FlowEdgeKind.RequestToHandler => r.Kind == RelationKind.Serves &&
                    string.Equals(r.From.SymbolId?.FullName ?? r.From.Name, item.From, StringComparison.Ordinal) &&
                    string.Equals(r.To.SymbolId?.FullName ?? r.To.Name, item.To, StringComparison.Ordinal),
                FlowEdgeKind.HandlerToRequest => r.Kind == RelationKind.Dispatches &&
                    r.Occurrences.Any(o => o.Rule == EvidenceRule.DispatchCallSite) &&
                    string.Equals(r.From.SymbolId?.FullName ?? r.From.Name, item.From, StringComparison.Ordinal) &&
                    string.Equals(r.To.SymbolId?.FullName ?? r.To.Name, item.To, StringComparison.Ordinal),
                FlowEdgeKind.HandlerToNotification => r.Kind == RelationKind.Publishes &&
                    r.Occurrences.Any(o => o.Rule == EvidenceRule.PublishCallSite) &&
                    string.Equals(r.From.SymbolId?.FullName ?? r.From.Name, item.From, StringComparison.Ordinal) &&
                    string.Equals(r.To.SymbolId?.FullName ?? r.To.Name, item.To, StringComparison.Ordinal),
                FlowEdgeKind.HandlerToDbContext => r.Kind == RelationKind.UsesData &&
                    string.Equals(r.From.SymbolId?.FullName ?? r.From.Name, item.From, StringComparison.Ordinal) &&
                    string.Equals(r.To.SymbolId?.FullName ?? r.To.Name, item.To, StringComparison.Ordinal),
                _ => false,
            }) > 0;
    }

    protected override void ClearCore()
    {
        var spine = _result.Spine;
        spine.ClearExplicitFlowKeys();
        spine.RemoveRelations(r =>
            r.Kind is RelationKind.UsesData ||
            (r.Kind == RelationKind.Dispatches && r.From.Kind == NodeReferenceKind.Endpoint) ||
            (r.Kind == RelationKind.Dispatches && r.Occurrences.All(o => o.Rule == EvidenceRule.DispatchCallSite)) ||
            (r.Kind == RelationKind.Publishes && r.Occurrences.All(o => o.Rule == EvidenceRule.PublishCallSite)));
    }

    private static string EndpointFromName(SpineRelation rel, ArchitectureSpine spine)
        => rel.From.EndpointId is not null && spine.TryGetEndpoint(rel.From.EndpointId, out var ep)
            ? ep!.Name
            : rel.From.Name;
}
