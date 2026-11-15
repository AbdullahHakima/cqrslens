namespace CqrsLens;

/// <summary>
/// Live projection of <see cref="AnalysisResult.ResolvedEdges"/> from
/// <see cref="RelationKind.UsesDataResolved"/> relations in <see cref="ArchitectureSpine"/>.
/// </summary>
public sealed class SpineResolvedEdgeList : SpineProjectedCollection<ResolvedEdge>
{
    private readonly AnalysisResult _result;

    internal SpineResolvedEdgeList(AnalysisResult result)
    {
        _result = result;
    }

    protected override long CacheStamp => _result.Spine.Version;

    protected override IReadOnlyList<ResolvedEdge> Project()
        => SpineQueries.ResolvedEdges(_result);

    protected override void AddCore(ResolvedEdge item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var resolver = _result.IdentityResolver;
        var from = resolver.ResolveComponentOrExternalRef(item.From, ComponentRole.Handler, item.ProjectName);
        var viaRef = resolver.ResolveInterfaceRef(item.Via);
        var to = resolver.ResolveComponentOrExternalRef(item.To, ComponentRole.DbContext, item.ProjectName);

        _result.Spine.AddOrMergeRelation(
            RelationKind.UsesDataResolved,
            from,
            to,
            SpineIdentityResolver.SyntheticOccurrence(
                EvidenceRule.ResolvedDbContextDependency,
                EvidenceOrigin.Declaration,
                item.Location,
                EvidenceStrength.Resolved,
                via: [viaRef],
                scopeProjectName: item.ProjectName));
    }

    protected override bool RemoveCore(ResolvedEdge item)
    {
        if (item is null)
            return false;

        return _result.Spine.RemoveRelations(r =>
            r.Kind == RelationKind.UsesDataResolved &&
            string.Equals(r.From.SymbolId?.FullName ?? r.From.Name, item.From, StringComparison.Ordinal) &&
            string.Equals(r.Via.Count > 0 ? r.Via[0].SymbolId?.FullName ?? r.Via[0].Name : "", item.Via, StringComparison.Ordinal) &&
            string.Equals(r.To.SymbolId?.FullName ?? r.To.Name, item.To, StringComparison.Ordinal)) > 0;
    }

    protected override void ClearCore()
        => _result.Spine.RemoveRelations(r => r.Kind == RelationKind.UsesDataResolved);
}
