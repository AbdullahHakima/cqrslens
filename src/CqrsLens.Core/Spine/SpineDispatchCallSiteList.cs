namespace CqrsLens;

/// <summary>
/// Live projection of <see cref="AnalysisResult.DispatchCallSites"/> from call-site evidenced
/// <see cref="RelationKind.Dispatches"/> and <see cref="RelationKind.Publishes"/> relations in
/// <see cref="ArchitectureSpine"/>.
/// </summary>
public sealed class SpineDispatchCallSiteList : SpineProjectedCollection<DispatchCallSite>
{
    private const string AnonymousSenderName = "<anonymous-sender>";
    private const string AnonymousSenderReason = "No declaring type on DispatchCallSite";
    private readonly AnalysisResult _result;

    internal SpineDispatchCallSiteList(AnalysisResult result)
    {
        _result = result;
    }

    protected override long CacheStamp => _result.Spine.Version;

    protected override IReadOnlyList<DispatchCallSite> Project()
        => SpineQueries.DispatchCallSites(_result.Spine);

    protected override void AddCore(DispatchCallSite item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var resolver = _result.IdentityResolver;
        var from = string.IsNullOrWhiteSpace(item.SenderFullName)
            ? NodeReference.ForUnresolved(AnonymousSenderName, AnonymousSenderReason)
            : resolver.ResolveTypeOrComponentRef(item.SenderFullName, item.ProjectName);

        var (kind, rule, targetRole) = item.TargetKind == DispatchTargetKind.Request
            ? (RelationKind.Dispatches, EvidenceRule.DispatchCallSite, ComponentRole.Request)
            : (RelationKind.Publishes, EvidenceRule.PublishCallSite, ComponentRole.Notification);

        var to = resolver.ResolveComponentOrExternalRef(item.TargetFullName, targetRole, item.ProjectName);
        var occ = new EvidenceOccurrence(
            rule,
            EvidenceOrigin.CallSite,
            item.Location,
            EvidenceStrength.Proven,
            locationOmittedReason: item.Location is null ? "Synthetic or hand-constructed DispatchCallSite" : null,
            scopeProjectName: item.ProjectName,
            isTestProject: item.IsTestProject);

        _result.Spine.AddOrMergeRelation(kind, from, to, occ);
    }

    protected override void ClearCore()
    {
        _result.Spine.RemoveRelations(r =>
            (r.Kind == RelationKind.Dispatches && r.Occurrences.All(o => o.Rule == EvidenceRule.DispatchCallSite)) ||
            (r.Kind == RelationKind.Publishes && r.Occurrences.All(o => o.Rule == EvidenceRule.PublishCallSite)));
    }
}
