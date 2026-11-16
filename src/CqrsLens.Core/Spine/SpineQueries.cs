using System.Runtime.CompilerServices;

namespace CqrsLens;

/// <summary>
/// Shared read-only projection queries over <see cref="ArchitectureSpine"/> consumed by
/// both reporting/CLI consumers and the legacy compatibility collection adapters.
/// </summary>
internal static class SpineQueries
{
    private const string AnonymousSenderName = "<anonymous-sender>";

    internal static IReadOnlyList<string> ProjectReferences(
        ArchitectureSpine spine,
        SpineIdentityResolver resolver,
        NodeReference owner)
    {
        var rels = spine.GetOutgoingRelationsInDiscoveryOrder(owner, RelationKind.References);
        if (rels.Count == 0)
            return Array.Empty<string>();
        if (rels.Count == 1)
            return new[] { FormatProjectReferenceTarget(resolver, rels[0].To) };

        var items = new List<string>(rels.Count);
        var seen = new HashSet<string>(rels.Count, StringComparer.Ordinal);
        for (var i = 0; i < rels.Count; i++)
        {
            var name = FormatProjectReferenceTarget(resolver, rels[i].To);
            if (seen.Add(name))
                items.Add(name);
        }

        return items;
    }

    internal static string FormatProjectReferenceTarget(SpineIdentityResolver resolver, NodeReference target)
        => target.ProjectId is not null
            ? resolver.ProjectNameFor(target.ProjectId) ?? StripSyntheticPrefix(target.ProjectId.Value)
            : target.Name;

    internal static IReadOnlyList<string> ProjectReferences(AnalysisResult result, ProjectInfo project)
    {
        if (project.SpineId is not null)
            return ProjectReferences(result.Spine, result.IdentityResolver, NodeReference.ForProject(project.SpineId));

        return project.ReferenceNames.ToList();
    }

    internal static IReadOnlyList<FlowEdge> FlowEdges(AnalysisResult result)
        => result.Spine.GetOrBuildFlowEdges(result.FlowAnalyzed, () => BuildFlowEdges(result));

    private static FlowEdge[] BuildFlowEdges(AnalysisResult result)
    {
        var spine = result.Spine;
        var resolver = result.IdentityResolver;
        var projected = new List<FlowEdge>();

        foreach (var rel in spine.RelationsInDiscoveryOrder)
        {
            switch (rel.Kind)
            {
                case RelationKind.Dispatches:
                    ProjectDispatches(rel, result.FlowAnalyzed, spine, resolver, projected);
                    break;

                case RelationKind.Serves:
                    if (result.FlowAnalyzed || spine.IsExplicitFlowKey(rel.Key))
                    {
                        ComponentNode? reqComp = rel.From.ComponentId is not null &&
                            spine.TryGetComponent(rel.From.ComponentId, out var c)
                            ? c
                            : null;
                        var first = rel.FirstOccurrence;
                        projected.Add(new FlowEdge
                        {
                            Kind = FlowEdgeKind.RequestToHandler,
                            From = rel.From.SymbolId?.FullName ?? rel.From.Name,
                            To = rel.To.SymbolId?.FullName ?? rel.To.Name,
                            ProjectName = reqComp is not null
                                ? resolver.ProjectNameFor(reqComp.Project) ?? first.ScopeProjectName
                                : first.ScopeProjectName,
                            Location = reqComp?.Location ?? first.Location,
                        });
                    }
                    break;

                case RelationKind.Publishes:
                {
                    EvidenceOccurrence? callSiteOcc = null;
                    foreach (var occ in rel.Occurrences)
                    {
                        if (occ.Rule == EvidenceRule.PublishCallSite)
                        {
                            callSiteOcc ??= occ;
                            break;
                        }
                    }

                    if (callSiteOcc is not null &&
                        (IsHandlerComponent(rel.From, spine) || spine.IsExplicitFlowKey(rel.Key)))
                    {
                        projected.Add(new FlowEdge
                        {
                            Kind = FlowEdgeKind.HandlerToNotification,
                            From = rel.From.SymbolId?.FullName ?? rel.From.Name,
                            To = rel.To.SymbolId?.FullName ?? rel.To.Name,
                            ProjectName = callSiteOcc.ScopeProjectName,
                            Location = callSiteOcc.Location,
                        });
                    }
                    break;
                }

                case RelationKind.UsesData:
                    if (result.FlowAnalyzed || spine.IsExplicitFlowKey(rel.Key))
                    {
                        ComponentNode? dbComp = rel.To.ComponentId is not null &&
                            spine.TryGetComponent(rel.To.ComponentId, out var c)
                            ? c
                            : null;
                        var first = rel.FirstOccurrence;
                        projected.Add(new FlowEdge
                        {
                            Kind = FlowEdgeKind.HandlerToDbContext,
                            From = rel.From.SymbolId?.FullName ?? rel.From.Name,
                            To = rel.To.SymbolId?.FullName ?? rel.To.Name,
                            ProjectName = dbComp is not null
                                ? resolver.ProjectNameFor(dbComp.Project) ?? first.ScopeProjectName
                                : first.ScopeProjectName,
                            Location = dbComp?.Location ?? first.Location,
                        });
                    }
                    break;
            }
        }

        if (projected.Count == 0)
            return Array.Empty<FlowEdge>();
        if (projected.Count == 1)
            return new[] { projected[0] };

        return DeduplicateAndSortFlowEdges(projected);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static FlowEdge[] DeduplicateAndSortFlowEdges(List<FlowEdge> projected)
    {
        var seen = new HashSet<(FlowEdgeKind, string, string)>(projected.Count);
        var unique = new List<FlowEdge>(projected.Count);
        for (var i = 0; i < projected.Count; i++)
        {
            var e = projected[i];
            if (seen.Add((e.Kind, e.From, e.To)))
                unique.Add(e);
        }

        if (unique.Count > 1)
        {
            unique.Sort(static (a, b) =>
            {
                var cmp = ((int)a.Kind).CompareTo((int)b.Kind);
                if (cmp != 0) return cmp;
                cmp = string.CompareOrdinal(a.From, b.From);
                return cmp != 0 ? cmp : string.CompareOrdinal(a.To, b.To);
            });
        }

        return unique.ToArray();
    }

    internal static IReadOnlyList<ResolvedEdge> ResolvedEdges(AnalysisResult result)
        => result.Spine.GetOrBuildResolvedEdges(() => BuildResolvedEdges(result));

    private static ResolvedEdge[] BuildResolvedEdges(AnalysisResult result)
    {
        var spine = result.Spine;
        if (!spine.HasRelation(RelationKind.UsesDataResolved))
            return Array.Empty<ResolvedEdge>();

        return BuildResolvedEdgesActive(result, spine);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ResolvedEdge[] BuildResolvedEdgesActive(AnalysisResult result, ArchitectureSpine spine)
    {
        var resolver = result.IdentityResolver;
        var projected = new List<ResolvedEdge>();

        foreach (var rel in spine.RelationsInDiscoveryOrder)
        {
            if (rel.Kind != RelationKind.UsesDataResolved)
                continue;

            ComponentNode? dbComp = rel.To.ComponentId is not null &&
                spine.TryGetComponent(rel.To.ComponentId, out var c)
                ? c
                : null;
            var first = rel.FirstOccurrence;
            var via = rel.Via.Count > 0 ? rel.Via[0].SymbolId?.FullName ?? rel.Via[0].Name : string.Empty;

            projected.Add(new ResolvedEdge
            {
                Kind = FlowEdgeKind.HandlerToDbContextResolved,
                From = rel.From.SymbolId?.FullName ?? rel.From.Name,
                Via = via,
                To = rel.To.SymbolId?.FullName ?? rel.To.Name,
                ProjectName = dbComp is not null
                    ? resolver.ProjectNameFor(dbComp.Project) ?? first.ScopeProjectName
                    : first.ScopeProjectName,
                Location = dbComp?.Location ?? first.Location,
            });
        }

        if (projected.Count == 0)
            return Array.Empty<ResolvedEdge>();
        if (projected.Count == 1)
            return new[] { projected[0] };

        var seen = new HashSet<(FlowEdgeKind, string, string, string)>(projected.Count);
        var unique = new List<ResolvedEdge>(projected.Count);
        for (var i = 0; i < projected.Count; i++)
        {
            var e = projected[i];
            if (seen.Add((e.Kind, e.From, e.Via, e.To)))
                unique.Add(e);
        }

        if (unique.Count > 1)
        {
            unique.Sort(static (a, b) =>
            {
                var cmp = string.CompareOrdinal(a.From, b.From);
                if (cmp != 0) return cmp;
                cmp = string.CompareOrdinal(a.Via, b.Via);
                return cmp != 0 ? cmp : string.CompareOrdinal(a.To, b.To);
            });
        }

        return unique.ToArray();
    }

    internal static IReadOnlyList<ServiceCallEdge> ServiceCallEdges(ArchitectureSpine spine)
        => spine.GetOrBuildServiceCallEdges(() => BuildServiceCallEdges(spine));

    private static ServiceCallEdge[] BuildServiceCallEdges(ArchitectureSpine spine)
    {
        if (!spine.HasRelation(RelationKind.Calls))
            return Array.Empty<ServiceCallEdge>();

        return BuildServiceCallEdgesActive(spine);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ServiceCallEdge[] BuildServiceCallEdgesActive(ArchitectureSpine spine)
    {
        var projected = new List<(ServiceCallEdge Edge, int Index)>();

        foreach (var rel in spine.RelationsInDiscoveryOrder)
        {
            if (rel.Kind != RelationKind.Calls)
                continue;

            EvidenceOccurrence? legacyOcc = null;
            foreach (var occ in rel.Occurrences)
            {
                if (occ.Rule == EvidenceRule.InjectedMemberInvocation)
                {
                    legacyOcc = occ;
                    break;
                }
            }

            if (legacyOcc is null)
                continue;

            var via = rel.Via.Count > 0 ? rel.Via[0].SymbolId?.FullName ?? rel.Via[0].Name : string.Empty;
            var method = rel.Method ?? rel.To.MemberName ?? string.Empty;
            var to = rel.To.SymbolId?.FullName ?? StripTrailingMember(rel.To.Name, rel.To.MemberName);

            projected.Add((new ServiceCallEdge
            {
                From = rel.From.SymbolId?.FullName ?? rel.From.Name,
                Via = via,
                Method = method,
                To = to,
                ProjectName = legacyOcc.ScopeProjectName,
                Location = legacyOcc.Location,
            }, projected.Count));
        }

        if (projected.Count == 0)
            return Array.Empty<ServiceCallEdge>();
        if (projected.Count == 1)
            return new[] { projected[0].Edge };

        projected.Sort(static (a, b) =>
        {
            var cmp = string.CompareOrdinal(a.Edge.From, b.Edge.From);
            if (cmp != 0) return cmp;
            cmp = string.CompareOrdinal(a.Edge.Via, b.Edge.Via);
            if (cmp != 0) return cmp;
            cmp = string.CompareOrdinal(a.Edge.Method, b.Edge.Method);
            if (cmp != 0) return cmp;
            cmp = string.CompareOrdinal(a.Edge.To, b.Edge.To);
            return cmp != 0 ? cmp : a.Index.CompareTo(b.Index);
        });

        var result = new ServiceCallEdge[projected.Count];
        for (var i = 0; i < projected.Count; i++)
            result[i] = projected[i].Edge;
        return result;
    }

    internal static IReadOnlyList<DispatchCallSite> DispatchCallSites(ArchitectureSpine spine)
        => spine.GetOrBuildDispatchCallSites(() => BuildDispatchCallSites(spine));

    private static DispatchCallSite[] BuildDispatchCallSites(ArchitectureSpine spine)
    {
        if (!spine.HasRelation(RelationKind.Dispatches) && !spine.HasRelation(RelationKind.Publishes))
            return Array.Empty<DispatchCallSite>();

        return BuildDispatchCallSitesActive(spine);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static DispatchCallSite[] BuildDispatchCallSitesActive(ArchitectureSpine spine)
    {
        var seen = new HashSet<(string, DispatchTargetKind, string?, string?, int?)>();
        var projected = new List<(DispatchCallSite Site, int Index)>();

        foreach (var rel in spine.RelationsInDiscoveryOrder)
        {
            DispatchTargetKind targetKind;
            EvidenceRule expectedRule;
            if (rel.Kind == RelationKind.Dispatches)
            {
                targetKind = DispatchTargetKind.Request;
                expectedRule = EvidenceRule.DispatchCallSite;
            }
            else if (rel.Kind == RelationKind.Publishes)
            {
                targetKind = DispatchTargetKind.Notification;
                expectedRule = EvidenceRule.PublishCallSite;
            }
            else
            {
                continue;
            }

            var senderFullName = rel.From.Kind == NodeReferenceKind.Unresolved &&
                                 string.Equals(rel.From.Name, AnonymousSenderName, StringComparison.Ordinal)
                ? null
                : rel.From.SymbolId?.FullName ?? rel.From.Name;

            var targetFullName = rel.To.SymbolId?.FullName ?? rel.To.Name;

            foreach (var occ in rel.Occurrences)
            {
                if (occ.Rule != expectedRule || occ.ExcludeFromDispatchCallSites)
                    continue;

                if (seen.Add((targetFullName, targetKind, senderFullName, occ.Location?.File, occ.Location?.Line)))
                {
                    projected.Add((new DispatchCallSite
                    {
                        TargetFullName = targetFullName,
                        TargetKind = targetKind,
                        SenderFullName = senderFullName,
                        ProjectName = occ.ScopeProjectName,
                        IsTestProject = occ.IsTestProject,
                        Location = occ.Location,
                    }, projected.Count));
                }
            }
        }

        if (projected.Count == 0)
            return Array.Empty<DispatchCallSite>();
        if (projected.Count == 1)
            return new[] { projected[0].Site };

        projected.Sort(static (a, b) =>
        {
            var cmp = ((int)a.Site.TargetKind).CompareTo((int)b.Site.TargetKind);
            if (cmp != 0) return cmp;
            cmp = string.CompareOrdinal(a.Site.TargetFullName, b.Site.TargetFullName);
            if (cmp != 0) return cmp;
            cmp = string.CompareOrdinal(a.Site.SenderFullName, b.Site.SenderFullName);
            return cmp != 0 ? cmp : a.Index.CompareTo(b.Index);
        });

        var result = new DispatchCallSite[projected.Count];
        for (var i = 0; i < projected.Count; i++)
            result[i] = projected[i].Site;
        return result;
    }

    private static void ProjectDispatches(
        SpineRelation rel,
        bool flowAnalyzed,
        ArchitectureSpine spine,
        SpineIdentityResolver resolver,
        List<FlowEdge> projected)
    {
        var isExplicit = spine.IsExplicitFlowKey(rel.Key);

        if (rel.From.Kind == NodeReferenceKind.Endpoint && rel.From.EndpointId is not null)
        {
            if (!flowAnalyzed && !isExplicit)
                return;

            if (spine.TryGetEndpoint(rel.From.EndpointId, out var ep))
            {
                projected.Add(new FlowEdge
                {
                    Kind = FlowEdgeKind.EndpointToRequest,
                    From = ep!.Name,
                    To = rel.To.SymbolId?.FullName ?? rel.To.Name,
                    ProjectName = resolver.ProjectNameFor(ep.Project) ?? rel.FirstOccurrence.ScopeProjectName,
                    Location = ep.Location ?? rel.FirstOccurrence.Location,
                });
            }
            return;
        }

        if (isExplicit && rel.Occurrences.Any(o => o.Rule == EvidenceRule.EndpointDispatch))
        {
            projected.Add(new FlowEdge
            {
                Kind = FlowEdgeKind.EndpointToRequest,
                From = rel.From.Name,
                To = rel.To.SymbolId?.FullName ?? rel.To.Name,
                ProjectName = rel.FirstOccurrence.ScopeProjectName,
                Location = rel.FirstOccurrence.Location,
            });
            return;
        }

        EvidenceOccurrence? dispatchOcc = null;
        foreach (var occ in rel.Occurrences)
        {
            if (occ.Rule == EvidenceRule.DispatchCallSite)
            {
                dispatchOcc ??= occ;
                break;
            }
        }

        if (dispatchOcc is not null && (IsHandlerComponent(rel.From, spine) || isExplicit))
        {
            projected.Add(new FlowEdge
            {
                Kind = FlowEdgeKind.HandlerToRequest,
                From = rel.From.SymbolId?.FullName ?? rel.From.Name,
                To = rel.To.SymbolId?.FullName ?? rel.To.Name,
                ProjectName = dispatchOcc.ScopeProjectName,
                Location = dispatchOcc.Location,
            });
        }
    }

    private static bool IsHandlerComponent(NodeReference reference, ArchitectureSpine spine)
    {
        if (reference.ComponentId is null || !spine.TryGetComponent(reference.ComponentId, out var comp))
            return false;
        return comp!.HasRole(ComponentRole.Handler) || comp.HasRole(ComponentRole.NotificationHandler);
    }

    private static string StripTrailingMember(string name, string? member)
    {
        if (string.IsNullOrEmpty(member))
            return name;
        var suffix = "." + member;
        return name.EndsWith(suffix, StringComparison.Ordinal)
            ? name[..^suffix.Length]
            : name;
    }

    private static string StripSyntheticPrefix(string value)
    {
        const string prefix = "__synthetic__/";
        return value.StartsWith(prefix, StringComparison.Ordinal) ? value[prefix.Length..] : value;
    }
}
