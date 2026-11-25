using System.Text;
using static CqrsLens.HtmlReportText;

namespace CqrsLens;

/// <summary>
/// Renders the zero-JS HTML Big Picture portal section (<c>id="system-at-a-glance"</c>):
/// Service Capability Cards, the C4 System Landscape diagram, and collapsible HTML5
/// <c>&lt;details&gt;</c> panels for cross-service integration flows and full system entrypoints.
/// </summary>
internal static class SystemOverviewHtmlRenderer
{
    internal static void Append(
        StringBuilder html,
        AnalysisResult result,
        DiagramRenderMode renderMode,
        string? focusService = null,
        bool excludeTests = false,
        string entrypointsAnchorId = "system-entrypoints")
    {
        if (!SystemViewFilter.HasSystemData(result))
            return;

        html.AppendLine("<section id=\"system-at-a-glance\" class=\"system-glance\">");
        html.AppendLine("<h2>System at a Glance</h2>");

        var filterNote = SystemViewFilter.DescribeFilters(result, excludeTests, focusService);
        if (filterNote is not null)
            html.AppendLine($"<p class=\"note\">Filtered view: {Encode(filterNote)}.</p>");

        var profiles = SystemViewFilter.VisibleProfiles(result, focusService, includeTests: false);
        AppendServiceCards(html, result, profiles);

        var landscape = DiagramBuilder.SystemLandscape(result, focusService);
        if (landscape is not null)
        {
            html.AppendLine("<h3>System Landscape</h3>");
            if (!DiagramBuilder.IsWithinMermaidLimits(landscape))
            {
                html.AppendLine("<p class=\"note\"><em>Diagram omitted: the system landscape exceeds the " +
                                "CqrsLens safety limit (45,000 characters / 400 edges).</em></p>");
            }
            else
            {
                html.AppendLine("<figure class=\"diagram\">");
                html.AppendLine(HtmlReportGenerator.RenderDiagram(landscape, renderMode));
                html.AppendLine("<figcaption>C4-style service, data store, and messaging landscape " +
                                "(test projects excluded).</figcaption>");
                html.AppendLine("</figure>");
            }
        }

        var flows = SystemViewFilter.VisibleFlows(result, focusService, excludeTests);
        if (flows.Count > 0)
            AppendIntegrationFlows(html, flows);

        var entrypoints = SystemViewFilter.VisibleEntrypoints(result, focusService, excludeTests);
        if (entrypoints.Count > 0)
            AppendEntrypoints(html, entrypoints, entrypointsAnchorId);

        html.AppendLine("</section>");
    }

    private static void AppendServiceCards(
        StringBuilder html,
        AnalysisResult result,
        IReadOnlyList<ServiceProfileInfo> profiles)
    {
        if (profiles.Count == 0)
            return;

        html.AppendLine("<h3>Services &amp; Capabilities</h3>");
        html.AppendLine("<div class=\"service-grid\">");
        foreach (var profile in profiles)
        {
            var roleClass = SystemViewFilter.RoleCssClass(profile.RuntimeRole);
            var roleLabel = SystemViewFilter.RoleLabel(profile.RuntimeRole);
            html.AppendLine("<article class=\"service-card\">");
            html.AppendLine("<div class=\"service-card-header\">" +
                            $"<strong>{Encode(profile.ProjectName)}</strong>" +
                            $"<span class=\"role-pill {roleClass}\">{Encode(roleLabel)}</span></div>");

            if (profile.Technologies.Count > 0)
            {
                html.AppendLine("<div class=\"tech-badges\">");
                foreach (var tech in profile.Technologies)
                    html.AppendLine($"<span class=\"tech-badge\">{Encode(tech)}</span>");
                html.AppendLine("</div>");
            }

            var metrics = FormatMetrics(result, profile);
            html.AppendLine("<div class=\"service-metrics\">");
            if (metrics.Count == 0)
            {
                html.AppendLine("<span class=\"meta-pill\">Support / Shared</span>");
            }
            else
            {
                foreach (var metric in metrics)
                    html.AppendLine($"<span class=\"meta-pill\">{Encode(metric)}</span>");
            }
            html.AppendLine("</div>");

            if (profile.EntryFiles.Count > 0)
            {
                html.Append("<div class=\"start-here\"><strong>Start Here:</strong> ");
                html.Append(string.Join(" ", profile.EntryFiles.Select(f => $"<code>{Encode(f)}</code>")));
                html.AppendLine("</div>");
            }

            html.AppendLine("</article>");
        }
        html.AppendLine("</div>");
    }

    private static List<string> FormatMetrics(AnalysisResult result, ServiceProfileInfo profile)
    {
        var list = new List<string>();
        var (httpCqrs, httpDirect, uiRoutes) = SystemViewFilter.ProjectEndpointCounts(result, profile.ProjectName);
        if (httpCqrs > 0 || httpDirect > 0)
            list.Add($"HTTP: {httpCqrs} CQRS / {httpDirect} Direct");
        else if (profile.HttpEndpointCount > 0)
            list.Add($"HTTP: {profile.HttpEndpointCount}");
        if (uiRoutes > 0)
            list.Add($"UI routes: {uiRoutes}");
        if (profile.GrpcEndpointCount > 0)
            list.Add($"gRPC: {profile.GrpcEndpointCount}");
        if (profile.WorkerCount > 0)
            list.Add($"Workers: {profile.WorkerCount}");
        if (profile.MessageConsumerCount > 0)
            list.Add($"Consumers: {profile.MessageConsumerCount}");
        if (profile.RequestCount > 0)
            list.Add($"Requests: {profile.RequestCount}");
        if (profile.DbContextCount > 0)
            list.Add($"DbContexts: {profile.DbContextCount}");
        if (profile.PublishedEventCount > 0 || profile.ConsumedEventCount > 0)
            list.Add($"Events: {profile.PublishedEventCount} pub / {profile.ConsumedEventCount} sub");
        return list;
    }

    private static void AppendIntegrationFlows(StringBuilder html, IReadOnlyList<IntegrationFlowEdge> flows)
    {
        html.AppendLine("<details class=\"portal-section\" open>");
        html.AppendLine($"<summary class=\"portal-summary\">Cross-Service Integration Flows ({flows.Count})</summary>");
        html.AppendLine("<div class=\"portal-body\">");
        html.AppendLine("<table><thead><tr><th>From</th><th>To</th><th>Kind</th><th>Details</th><th>Source</th></tr></thead><tbody>");
        foreach (var flow in flows)
        {
            var detail = !string.IsNullOrWhiteSpace(flow.MessageType) &&
                         !string.Equals(flow.MessageType, flow.Label, StringComparison.Ordinal)
                ? $"{flow.Label} ({flow.MessageType})"
                : flow.Label;
            var source = flow.Location is null
                ? "—"
                : $"<code>{Encode($"{flow.Location.File}:{flow.Location.Line}")}</code>";
            html.AppendLine($"<tr><td><code>{Encode(flow.FromProject)}</code></td>" +
                            $"<td><code>{Encode(flow.ToProject)}</code></td>" +
                            $"<td>{Encode(SystemViewFilter.FlowKindLabel(flow.Kind))}</td>" +
                            $"<td>{Encode(detail)}</td>" +
                            $"<td>{source}</td></tr>");
        }
        html.AppendLine("</tbody></table>");
        html.AppendLine("</div>");
        html.AppendLine("</details>");
    }

    private static void AppendEntrypoints(
        StringBuilder html,
        IReadOnlyList<SystemEntrypointInfo> entrypoints,
        string anchorId)
    {
        var groups = entrypoints
            .GroupBy(e => string.IsNullOrWhiteSpace(e.ProjectName) ? MermaidDiagramText.UnassignedProject : e.ProjectName!)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        html.AppendLine($"<div id=\"{Encode(anchorId)}\">");
        html.AppendLine($"<h3>System Entrypoints ({entrypoints.Count})</h3>");
        var expandAll = groups.Count <= 4;

        foreach (var group in groups)
        {
            var ordered = group
                .OrderBy(e => e.Kind)
                .ThenBy(e => e.RouteOrTrigger, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.Method, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var noun = ordered.Count == 1 ? "entrypoint" : "entrypoints";
            var openAttr = expandAll ? " open" : "";

            html.AppendLine($"<details class=\"portal-section\"{openAttr}>");
            html.AppendLine($"<summary class=\"portal-summary\"><code>{Encode(group.Key)}</code> — {ordered.Count} {noun}</summary>");
            html.AppendLine("<div class=\"portal-body\">");
            html.AppendLine("<table><thead><tr><th>Method</th><th>Route / Trigger</th><th>Entrypoint</th>" +
                            "<th>Style</th><th>Target Requests</th><th>Services &amp; Data</th></tr></thead><tbody>");
            foreach (var entry in ordered)
            {
                var route = string.IsNullOrWhiteSpace(entry.RouteOrTrigger)
                    ? "—"
                    : $"<code>{Encode(entry.RouteOrTrigger)}</code>";
                var name = entry.IsInferred ? $"{entry.Name} (inferred)" : entry.Name;
                var targets = entry.TargetRequests.Count > 0
                    ? Encode(SystemViewFilter.JoinShort(entry.TargetRequests, 4))
                    : "—";
                var parts = SystemViewFilter.DownstreamParts(entry);
                var downstream = parts.Count > 0
                    ? Encode(string.Join(" · ", parts))
                    : "—";

                html.AppendLine($"<tr><td>{Encode(entry.Method)}</td><td>{route}</td>" +
                                $"<td>{Encode(name)}</td><td>{Encode(SystemViewFilter.StyleLabel(entry.ExecutionStyle))}</td>" +
                                $"<td>{targets}</td><td>{downstream}</td></tr>");
            }
            html.AppendLine("</tbody></table>");
            html.AppendLine("</div>");
            html.AppendLine("</details>");
        }
        html.AppendLine("</div>");
    }
}
