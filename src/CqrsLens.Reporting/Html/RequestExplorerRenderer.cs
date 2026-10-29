using System.Text;

namespace CqrsLens;

/// <summary>Renders the interactive CQRS explorer cards.</summary>
internal static class RequestExplorerRenderer
{
    internal static void Render(
        StringBuilder html,
        AnalysisResult result,
        DiagramRenderMode renderMode,
        RequestExplorerIndex index)
    {
        var commandCount = result.Requests.Count(r => string.Equals(r.Kind, "Command", StringComparison.Ordinal));
        var queryCount = result.Requests.Count(r => string.Equals(r.Kind, "Query", StringComparison.Ordinal));
        var issueCount = result.Requests.Count(r => index.ChecksByTarget.ContainsKey(r.FullName));

        html.AppendLine("<h2>Interactive CQRS Explorer</h2>");
        html.AppendLine("<p class=\"note\">Click any request in the table or diagram (or expand a card below) to inspect its contract, handlers, validators, and source file locations.</p>");
        html.AppendLine("<section class=\"cqrs-explorer\" aria-label=\"Interactive CQRS explorer\">");

        // Pure CSS radio filter inputs (work in both Mermaid and zero-JS SVG modes)
        html.AppendLine("<input type=\"radio\" name=\"cqrs-filter\" id=\"cqrs-filter-all\" class=\"filter-radio\" checked>");
        html.AppendLine("<input type=\"radio\" name=\"cqrs-filter\" id=\"cqrs-filter-command\" class=\"filter-radio\">");
        html.AppendLine("<input type=\"radio\" name=\"cqrs-filter\" id=\"cqrs-filter-query\" class=\"filter-radio\">");
        html.AppendLine("<input type=\"radio\" name=\"cqrs-filter\" id=\"cqrs-filter-issues\" class=\"filter-radio\">");

        html.AppendLine("<div class=\"explorer-toolbar\">");
        html.AppendLine("<div class=\"filter-pills\" role=\"group\" aria-label=\"Filter requests by type\">");
        html.AppendLine($"<label for=\"cqrs-filter-all\" class=\"filter-pill\">All ({result.Requests.Count})</label>");
        html.AppendLine($"<label for=\"cqrs-filter-command\" class=\"filter-pill\">Commands ({commandCount})</label>");
        html.AppendLine($"<label for=\"cqrs-filter-query\" class=\"filter-pill\">Queries ({queryCount})</label>");
        html.AppendLine($"<label for=\"cqrs-filter-issues\" class=\"filter-pill\">With Findings ({issueCount})</label>");
        html.AppendLine("</div>");

        if (renderMode == DiagramRenderMode.Mermaid)
        {
            html.AppendLine("<div class=\"explorer-actions\">");
            html.AppendLine("<input type=\"search\" id=\"cqrs-explorer-search\" class=\"explorer-search\" placeholder=\"Filter by request, handler, validator, project, or file…\" aria-label=\"Filter requests\">");
            html.AppendLine("<button type=\"button\" id=\"cqrs-explorer-expand\" class=\"explorer-btn\">Expand all</button>");
            html.AppendLine("<button type=\"button\" id=\"cqrs-explorer-collapse\" class=\"explorer-btn\">Collapse all</button>");
            html.AppendLine("</div>");
        }
        html.AppendLine("</div>");

        html.AppendLine("<div class=\"explorer-list\">");
        foreach (var r in result.Requests)
            RenderItem(html, r, index);
        html.AppendLine("</div>"); // .explorer-list
        html.AppendLine("</section>");
    }

    private static void RenderItem(StringBuilder html, CqrsRequest r, RequestExplorerIndex index)
    {
        var anchor = index.Anchors[r];
        var kindClass = r.Kind.ToLowerInvariant() switch
        {
            "command" => "command",
            "query" => "query",
            _ => "request",
        };
        index.ChecksByTarget.TryGetValue(r.FullName, out var reqChecks);
        var hasIssues = reqChecks is { Count: > 0 };
        var issueAttr = hasIssues ? "true" : "false";
        var searchBlob = index.BuildSearchBlob(r);

        html.AppendLine(
            $"<details class=\"explorer-item\" id=\"{HtmlReportText.Encode(anchor)}\" " +
            $"data-kind=\"{HtmlReportText.Encode(kindClass)}\" data-issues=\"{issueAttr}\" " +
            $"data-request-name=\"{HtmlReportText.Encode(r.Name)}\" data-request-fullname=\"{HtmlReportText.Encode(r.FullName)}\" " +
            $"data-search=\"{HtmlReportText.Encode(searchBlob)}\">");

        WriteSummary(html, r, kindClass, hasIssues, reqChecks);

        html.AppendLine("<div class=\"explorer-body\">");
        html.AppendLine("<div class=\"explorer-grid\">");
        WriteContractPanel(html, r);
        WriteHandlersPanel(html, r, index);
        WriteValidatorsPanel(html, r, index);
        html.AppendLine("</div>"); // .explorer-grid

        if (hasIssues)
        {
            html.AppendLine("<ul class=\"checks explorer-checks\">");
            foreach (var check in reqChecks!)
                HtmlCheckRenderer.Append(html, check, check.IsKnown);
            html.AppendLine("</ul>");
        }

        html.AppendLine("</div>"); // .explorer-body
        html.AppendLine("</details>");
    }

    private static void WriteSummary(
        StringBuilder html,
        CqrsRequest r,
        string kindClass,
        bool hasIssues,
        List<CheckFinding>? reqChecks)
    {
        html.Append("<summary class=\"explorer-summary\">");
        html.Append($"<span class=\"badge badge-{HtmlReportText.Encode(kindClass)}\">{HtmlReportText.Encode(r.Kind)}</span>");
        html.Append($"<span class=\"explorer-title\"><strong>{HtmlReportText.Encode(r.Name)}</strong>");
        if (!string.IsNullOrEmpty(r.ResponseType))
            html.Append($" <span class=\"explorer-response\">→ <code>{HtmlReportText.Encode(r.ResponseType)}</code></span>");
        html.Append("</span>");
        html.Append("<span class=\"explorer-meta\">");
        if (!string.IsNullOrEmpty(r.ProjectName))
            html.Append($"<span class=\"meta-pill\">{HtmlReportText.Encode(r.ProjectName)}</span>");
        if (hasIssues)
        {
            var hasErr = reqChecks!.Any(c => c.Severity == CheckSeverity.Error);
            var statusCls = hasErr ? "status-error" : "status-warning";
            var statusLabel = hasErr ? "❌ Check error" : "⚠️ Check warning";
            html.Append($"<span class=\"meta-pill {statusCls}\">{statusLabel}</span>");
        }
        html.Append("</span>");
        html.AppendLine("</summary>");
    }

    private static void WriteContractPanel(StringBuilder html, CqrsRequest r)
    {
        html.AppendLine("<div class=\"explorer-panel\">");
        html.AppendLine("<h4>Request Contract</h4>");
        html.AppendLine("<dl class=\"explorer-dl\">");
        html.AppendLine($"<dt>Full name</dt><dd><code>{HtmlReportText.Encode(r.FullName)}</code></dd>");
        html.AppendLine($"<dt>Kind</dt><dd>{HtmlReportText.Encode(r.Kind)}{(string.IsNullOrEmpty(r.ViaMarker) ? "" : $" (via <code>{HtmlReportText.Encode(r.ViaMarker)}</code>)")}</dd>");
        html.AppendLine($"<dt>Response</dt><dd><code>{HtmlReportText.Encode(r.ResponseType ?? "void")}</code></dd>");
        html.AppendLine($"<dt>Project</dt><dd>{HtmlReportText.Encode(r.ProjectName ?? "—")}</dd>");
        html.AppendLine($"<dt>Source file</dt><dd>{RequestExplorerIndex.FormatLocation(r.Location)}</dd>");
        if (r.Entrypoints.Count > 0)
            html.AppendLine($"<dt>Entrypoints</dt><dd>{string.Join(", ", r.Entrypoints.Select(e => $"<code>{HtmlReportText.Encode(e)}</code>"))}</dd>");
        if (r.Properties.Count > 0)
            html.AppendLine($"<dt>Payload</dt><dd>{string.Join(", ", r.Properties.Select(p => $"<code>{HtmlReportText.Encode(p.Name)}: {HtmlReportText.Encode(p.Type)}</code>"))}</dd>");
        if (r.TouchedEntities.Count > 0)
            html.AppendLine($"<dt>DbSets</dt><dd>{string.Join(", ", r.TouchedEntities.Select(e => $"<code>{HtmlReportText.Encode(e)}</code>"))}</dd>");
        if (r.PublishedNotifications.Count > 0)
            html.AppendLine($"<dt>Events</dt><dd>{string.Join(", ", r.PublishedNotifications.Select(n => $"<code>{HtmlReportText.Encode(CSharpScanner.SimpleName(n))}</code>"))}</dd>");
        html.AppendLine("</dl>");
        html.AppendLine("</div>");
    }

    private static void WriteHandlersPanel(StringBuilder html, CqrsRequest r, RequestExplorerIndex index)
    {
        html.AppendLine("<div class=\"explorer-panel\">");
        html.AppendLine($"<h4>Handlers ({r.Handlers.Count})</h4>");
        if (r.Handlers.Count == 0)
        {
            html.AppendLine("<p class=\"explorer-empty\">No handler linked.</p>");
        }
        else
        {
            html.AppendLine("<ul class=\"explorer-links\">");
            foreach (var handlerName in r.Handlers)
            {
                var hInfo = index.ResolveHandler(handlerName);
                var hDisplay = CSharpScanner.SimpleName(handlerName);
                var hLoc = hInfo?.Location is not null ? $" · {RequestExplorerIndex.FormatLocation(hInfo.Location)}" : "";
                var hProj = !string.IsNullOrEmpty(hInfo?.ProjectName) ? $" <span class=\"meta-sub\">({HtmlReportText.Encode(hInfo!.ProjectName)})</span>" : "";
                var hDeps = hInfo is { Dependencies.Count: > 0 } ? $"<br><span class=\"meta-sub\">Deps: <code>{HtmlReportText.Encode(string.Join(", ", hInfo.Dependencies))}</code></span>" : "";
                html.AppendLine($"<li><strong>{HtmlReportText.Encode(hDisplay)}</strong>{hProj}<br><span class=\"meta-sub\"><code>{HtmlReportText.Encode(hInfo?.FullName ?? handlerName)}</code>{hLoc}</span>{hDeps}</li>");
            }
            html.AppendLine("</ul>");
        }
        html.AppendLine("</div>");
    }

    private static void WriteValidatorsPanel(StringBuilder html, CqrsRequest r, RequestExplorerIndex index)
    {
        html.AppendLine("<div class=\"explorer-panel\">");
        html.AppendLine($"<h4>Validators ({r.Validators.Count})</h4>");
        if (r.Validators.Count == 0)
        {
            html.AppendLine("<p class=\"explorer-empty\">No validators linked.</p>");
        }
        else
        {
            html.AppendLine("<ul class=\"explorer-links\">");
            foreach (var validatorName in r.Validators)
            {
                var vInfo = index.ResolveValidator(validatorName);
                var vDisplay = CSharpScanner.SimpleName(validatorName);
                var vLoc = vInfo?.Location is not null ? $" · {RequestExplorerIndex.FormatLocation(vInfo.Location)}" : "";
                var vProj = !string.IsNullOrEmpty(vInfo?.ProjectName) ? $" <span class=\"meta-sub\">({HtmlReportText.Encode(vInfo!.ProjectName)})</span>" : "";
                html.AppendLine($"<li><strong>{HtmlReportText.Encode(vDisplay)}</strong>{vProj}<br><span class=\"meta-sub\"><code>{HtmlReportText.Encode(vInfo?.FullName ?? validatorName)}</code>{vLoc}</span></li>");
            }
            html.AppendLine("</ul>");
        }
        html.AppendLine("</div>");
    }
}
