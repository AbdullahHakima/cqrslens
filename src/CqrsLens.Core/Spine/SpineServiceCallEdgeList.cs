namespace CqrsLens;

/// <summary>
/// Live projection of <see cref="AnalysisResult.ServiceCallEdges"/> from
/// <see cref="RelationKind.Calls"/> relations in <see cref="ArchitectureSpine"/>.
/// </summary>
public sealed class SpineServiceCallEdgeList : SpineProjectedCollection<ServiceCallEdge>
{
    private readonly AnalysisResult _result;

    internal SpineServiceCallEdgeList(AnalysisResult result)
    {
        _result = result;
    }

    protected override long CacheStamp => _result.Spine.Version;

    protected override IReadOnlyList<ServiceCallEdge> Project()
        => SpineQueries.ServiceCallEdges(_result.Spine);

    protected override void AddCore(ServiceCallEdge item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var resolver = _result.IdentityResolver;
        var from = resolver.ResolveTypeOrComponentRef(item.From, item.ProjectName);
        var viaRef = string.Equals(item.Via, item.To, StringComparison.Ordinal)
            ? resolver.ResolveTypeOrComponentRef(item.Via, item.ProjectName)
            : resolver.ResolveInterfaceRef(item.Via);
        var to = resolver.ResolveMethodTargetRef(item.To, item.Method, item.ProjectName);
        var strength = string.Equals(item.Via, item.To, StringComparison.Ordinal)
            ? EvidenceStrength.Proven
            : EvidenceStrength.Resolved;

        _result.Spine.AddOrMergeRelation(
            RelationKind.Calls,
            from,
            to,
            SpineIdentityResolver.SyntheticOccurrence(
                EvidenceRule.InjectedMemberInvocation,
                EvidenceOrigin.CallSite,
                item.Location,
                strength,
                via: [viaRef],
                scopeProjectName: item.ProjectName),
            method: item.Method);
    }

    protected override bool RemoveCore(ServiceCallEdge item)
    {
        if (item is null)
            return false;

        return _result.Spine.RemoveRelations(r =>
            r.Kind == RelationKind.Calls &&
            string.Equals(r.From.SymbolId?.FullName ?? r.From.Name, item.From, StringComparison.Ordinal) &&
            string.Equals(r.Via.Count > 0 ? r.Via[0].SymbolId?.FullName ?? r.Via[0].Name : "", item.Via, StringComparison.Ordinal) &&
            string.Equals(r.Method ?? r.To.MemberName ?? "", item.Method, StringComparison.Ordinal) &&
            string.Equals(r.To.SymbolId?.FullName ?? StripTrailingMember(r.To.Name, r.To.MemberName), item.To, StringComparison.Ordinal)) > 0;
    }

    protected override void ClearCore()
        => _result.Spine.RemoveRelations(r => r.Kind == RelationKind.Calls);

    private static string StripTrailingMember(string name, string? member)
    {
        if (string.IsNullOrEmpty(member))
            return name;
        var suffix = "." + member;
        return name.EndsWith(suffix, StringComparison.Ordinal)
            ? name[..^suffix.Length]
            : name;
    }
}
