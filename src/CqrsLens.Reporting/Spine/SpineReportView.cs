namespace CqrsLens;

/// <summary>
/// Presentation projection of <see cref="ArchitectureSpine"/> shared by the Markdown,
/// HTML, and docs-export report writers so every format renders identical data in
/// deterministic sort order.
/// </summary>
internal static class SpineReportView
{
    private static readonly ComponentRole[] OrderedRoles =
    {
        ComponentRole.Request,
        ComponentRole.Handler,
        ComponentRole.Notification,
        ComponentRole.NotificationHandler,
        ComponentRole.Validator,
        ComponentRole.Behavior,
        ComponentRole.DbContext,
        ComponentRole.Entity,
        ComponentRole.Controller,
        ComponentRole.Service,
        ComponentRole.Repository,
    };

    internal sealed record RoleRow(
        string RoleName,
        bool IsMappingDerivedEndpoint,
        int ComponentCount,
        int EvidenceOccurrences);

    internal sealed record RelationSummaryRow(
        string KindName,
        int RelationCount,
        int OccurrenceCount);

    internal sealed record EndpointRow(
        EndpointId Id,
        string Method,
        string Route,
        string Endpoint,
        string Project,
        string DispatchesTo,
        bool Inferred,
        SourceLocation? Location);

    internal sealed record ComponentIndexRow(
        ComponentId Id,
        string Component,
        string Roles,
        string Project,
        SourceLocation? Location);

    internal sealed record ConnectedComponentRow(
        ComponentId Id,
        string Component,
        int Incoming,
        int Outgoing,
        int Total);

    internal sealed record Summary(
        int ComponentCount,
        int EndpointCount,
        int RelationCount,
        int TotalRelationOccurrences,
        int UnprovenCount,
        IReadOnlyList<RoleRow> Roles,
        IReadOnlyList<RelationSummaryRow> RelationsSummary,
        IReadOnlyList<EndpointRow> Endpoints,
        IReadOnlyList<ComponentIndexRow> Components,
        IReadOnlyList<ConnectedComponentRow> MostConnected);

    internal static Summary Build(AnalysisResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var spine = result.Spine;
        var components = spine.Components;
        var endpoints = spine.Endpoints;
        var relations = spine.Relations;
        var projectsById = DocsFormatting.BuildProjectsById(spine);

        var totalOccurrences = 0;
        var outgoingCounts = new Dictionary<ComponentId, int>();
        var incomingCounts = new Dictionary<ComponentId, int>();
        var relationsByKind = new Dictionary<RelationKind, (int Relations, int Occurrences)>();
        var dispatchesByEndpoint = new Dictionary<EndpointId, List<string>>();

        foreach (var rel in relations)
        {
            totalOccurrences += rel.OccurrenceCount;

            relationsByKind.TryGetValue(rel.Kind, out var kindCounts);
            relationsByKind[rel.Kind] = (kindCounts.Relations + 1, kindCounts.Occurrences + rel.OccurrenceCount);

            if (rel.From.ComponentId is not null)
            {
                outgoingCounts.TryGetValue(rel.From.ComponentId, out var count);
                outgoingCounts[rel.From.ComponentId] = count + 1;
            }

            var targetComp = rel.To.ComponentId;
            if (targetComp is null && rel.To.SymbolId is not null)
            {
                var candidate = ComponentId.FromSymbol(rel.To.SymbolId);
                if (spine.TryGetComponent(candidate, out _))
                    targetComp = candidate;
            }
            if (targetComp is not null)
            {
                incomingCounts.TryGetValue(targetComp, out var count);
                incomingCounts[targetComp] = count + 1;
            }

            if (rel.Kind == RelationKind.Dispatches && rel.From.EndpointId is not null)
            {
                if (!dispatchesByEndpoint.TryGetValue(rel.From.EndpointId, out var targets))
                {
                    targets = new List<string>();
                    dispatchesByEndpoint[rel.From.EndpointId] = targets;
                }
                if (!targets.Contains(rel.To.Name, StringComparer.Ordinal))
                    targets.Add(rel.To.Name);
            }
        }

        var roleComponentCounts = new Dictionary<ComponentRole, int>();
        var roleOccurrenceCounts = new Dictionary<ComponentRole, int>();
        foreach (var comp in components)
        {
            foreach (var roleEv in comp.Roles)
            {
                roleComponentCounts.TryGetValue(roleEv.Role, out var c);
                roleComponentCounts[roleEv.Role] = c + 1;

                roleOccurrenceCounts.TryGetValue(roleEv.Role, out var ev);
                roleOccurrenceCounts[roleEv.Role] = ev + roleEv.Evidence.Count;
            }
        }

        var roleRows = new List<RoleRow>(OrderedRoles.Length + 1);
        foreach (var role in OrderedRoles)
        {
            roleComponentCounts.TryGetValue(role, out var compCount);
            roleOccurrenceCounts.TryGetValue(role, out var evCount);
            roleRows.Add(new RoleRow(JsonSpineNames.ComponentRoleName(role), false, compCount, evCount));
        }

        var endpointEvidenceCount = 0;
        foreach (var ep in endpoints)
            endpointEvidenceCount += ep.Evidence.Count;
        roleRows.Add(new RoleRow("endpoint", true, endpoints.Count, endpointEvidenceCount));

        var relationsSummary = relationsByKind
            .OrderBy(p => (int)p.Key)
            .Select(p => new RelationSummaryRow(
                JsonSpineNames.RelationKindName(p.Key),
                p.Value.Relations,
                p.Value.Occurrences))
            .ToList();

        var endpointRows = endpoints
            .Select(ep =>
            {
                var projectName = projectsById.TryGetValue(ep.Project, out var proj) ? proj.Name : ep.Project.Value;
                var dispatchesTo = string.Empty;
                if (dispatchesByEndpoint.TryGetValue(ep.Id, out var targets) && targets.Count > 0)
                {
                    if (targets.Count > 1)
                        targets.Sort(StringComparer.Ordinal);
                    dispatchesTo = string.Join(", ", targets);
                }

                return new EndpointRow(
                    ep.Id,
                    ep.HttpMethod,
                    ep.Route,
                    ep.Name,
                    projectName,
                    dispatchesTo,
                    ep.IsInferred,
                    ep.Location);
            })
            .OrderBy(r => r.Route, StringComparer.Ordinal)
            .ThenBy(r => r.Method, StringComparer.Ordinal)
            .ThenBy(r => r.Project, StringComparer.Ordinal)
            .ThenBy(r => r.Endpoint, StringComparer.Ordinal)
            .ThenBy(r => r.Id)
            .ToList();

        var componentRows = components
            .Select(comp =>
            {
                var projectName = projectsById.TryGetValue(comp.Project, out var proj) ? proj.Name : comp.Project.Value;
                var roles = string.Join(", ", comp.Roles.Select(r => JsonSpineNames.ComponentRoleName(r.Role)));
                return new ComponentIndexRow(
                    comp.Id,
                    comp.FullName,
                    roles,
                    projectName,
                    comp.Location);
            })
            .OrderBy(r => r.Project, StringComparer.Ordinal)
            .ThenBy(r => r.Component, StringComparer.Ordinal)
            .ThenBy(r => r.Id)
            .ToList();

        var mostConnected = components
            .Select(comp =>
            {
                incomingCounts.TryGetValue(comp.Id, out var incoming);
                outgoingCounts.TryGetValue(comp.Id, out var outgoing);
                return new ConnectedComponentRow(
                    comp.Id,
                    comp.FullName,
                    incoming,
                    outgoing,
                    incoming + outgoing);
            })
            .OrderByDescending(r => r.Total)
            .ThenBy(r => r.Component, StringComparer.Ordinal)
            .ThenBy(r => r.Id)
            .Take(25)
            .ToList();

        return new Summary(
            components.Count,
            endpoints.Count,
            relations.Count,
            totalOccurrences,
            spine.Unproven.TotalCount,
            roleRows,
            relationsSummary,
            endpointRows,
            componentRows,
            mostConnected);
    }
}
