using System.Text;
using static CqrsLens.HtmlReportText;

namespace CqrsLens;

/// <summary>Renders the requests table and the interactive CQRS explorer.</summary>
internal static class HtmlRequestExplorerRenderer
{
    internal static void Render(
        StringBuilder html,
        AnalysisResult result,
        DiagramRenderMode renderMode)
    {
        html.AppendLine("<h2>Requests</h2>");
        if (result.Requests.Count == 0)
        {
            html.AppendLine("<p><em>No MediatR requests found.</em></p>");
            return;
        }

        var usedAnchors = new HashSet<string>(StringComparer.Ordinal);
        var anchors = new Dictionary<CqrsRequest, string>();
        foreach (var r in result.Requests)
        {
            var baseId = "explorer-" + DiagramBuilder.MermaidId(r.FullName);
            var id = baseId;
            var suffix = 2;
            while (!usedAnchors.Add(id))
                id = $"{baseId}-{suffix++}";
            anchors[r] = id;
        }

        var handlersByFullName = result.Handlers
            .GroupBy(h => h.FullName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var handlersBySimpleName = result.Handlers
            .GroupBy(h => h.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var validatorsByFullName = result.Validators
            .GroupBy(v => v.FullName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var validatorsBySimpleName = result.Validators
            .GroupBy(v => v.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var checksByTarget = result.Checks
            .Where(c => !string.IsNullOrEmpty(c.Target))
            .GroupBy(c => c.Target!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        html.AppendLine("<table class=\"requests-table\">");
        html.AppendLine("<thead><tr><th>Request</th><th>Type</th><th>Response</th><th>Handler</th><th>Validators</th><th>Project</th></tr></thead>");
        html.AppendLine("<tbody>");
        foreach (var r in result.Requests)
        {
            var anchor = anchors[r];
            var searchBlob = BuildSearchBlob(r, handlersByFullName, handlersBySimpleName, validatorsByFullName, validatorsBySimpleName);
            html.AppendLine($"<tr data-search=\"{Encode(searchBlob)}\">" +
                $"<td><a class=\"req-link\" href=\"#{Encode(anchor)}\">{Encode(r.Name)}</a></td>" +
                $"<td>{Encode(r.Kind)}</td>" +
                $"<td>{Encode(r.ResponseType ?? "—")}</td>" +
                $"<td>{Encode(r.Handlers.Count > 0 ? string.Join(", ", r.Handlers.Select(CSharpScanner.SimpleName)) : "—")}</td>" +
                $"<td>{Encode(r.Validators.Count > 0 ? string.Join(", ", r.Validators.Select(CSharpScanner.SimpleName)) : "—")}</td>" +
                $"<td>{Encode(r.ProjectName ?? "—")}</td>" +
                "</tr>");
        }
        html.AppendLine("</tbody>");
        html.AppendLine("</table>");

        // ---- Interactive CQRS Explorer ---------------------------------------
        var commandCount = result.Requests.Count(r => string.Equals(r.Kind, "Command", StringComparison.Ordinal));
        var queryCount = result.Requests.Count(r => string.Equals(r.Kind, "Query", StringComparison.Ordinal));
        var issueCount = result.Requests.Count(r => checksByTarget.ContainsKey(r.FullName));

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
        {
            var anchor = anchors[r];
            var kindClass = r.Kind.ToLowerInvariant() switch
            {
                "command" => "command",
                "query" => "query",
                _ => "request",
            };
            checksByTarget.TryGetValue(r.FullName, out var reqChecks);
            var hasIssues = reqChecks is { Count: > 0 };
            var issueAttr = hasIssues ? "true" : "false";
            var searchBlob = BuildSearchBlob(r, handlersByFullName, handlersBySimpleName, validatorsByFullName, validatorsBySimpleName);

            html.AppendLine(
                $"<details class=\"explorer-item\" id=\"{Encode(anchor)}\" " +
                $"data-kind=\"{Encode(kindClass)}\" data-issues=\"{issueAttr}\" " +
                $"data-request-name=\"{Encode(r.Name)}\" data-request-fullname=\"{Encode(r.FullName)}\" " +
                $"data-search=\"{Encode(searchBlob)}\">");

            html.Append("<summary class=\"explorer-summary\">");
            html.Append($"<span class=\"badge badge-{Encode(kindClass)}\">{Encode(r.Kind)}</span>");
            html.Append($"<span class=\"explorer-title\"><strong>{Encode(r.Name)}</strong>");
            if (!string.IsNullOrEmpty(r.ResponseType))
                html.Append($" <span class=\"explorer-response\">→ <code>{Encode(r.ResponseType)}</code></span>");
            html.Append("</span>");
            html.Append("<span class=\"explorer-meta\">");
            if (!string.IsNullOrEmpty(r.ProjectName))
                html.Append($"<span class=\"meta-pill\">{Encode(r.ProjectName)}</span>");
            if (hasIssues)
            {
                var hasErr = reqChecks!.Any(c => c.Severity == CheckSeverity.Error);
                var statusCls = hasErr ? "status-error" : "status-warning";
                var statusLabel = hasErr ? "❌ Check error" : "⚠️ Check warning";
                html.Append($"<span class=\"meta-pill {statusCls}\">{statusLabel}</span>");
            }
            html.Append("</span>");
            html.AppendLine("</summary>");

            html.AppendLine("<div class=\"explorer-body\">");
            html.AppendLine("<div class=\"explorer-grid\">");

            // Column 1: Request contract & source location
            html.AppendLine("<div class=\"explorer-panel\">");
            html.AppendLine("<h4>Request Contract</h4>");
            html.AppendLine("<dl class=\"explorer-dl\">");
            html.AppendLine($"<dt>Full name</dt><dd><code>{Encode(r.FullName)}</code></dd>");
            html.AppendLine($"<dt>Kind</dt><dd>{Encode(r.Kind)}{(string.IsNullOrEmpty(r.ViaMarker) ? "" : $" (via <code>{Encode(r.ViaMarker)}</code>)")}</dd>");
            html.AppendLine($"<dt>Response</dt><dd><code>{Encode(r.ResponseType ?? "void")}</code></dd>");
            html.AppendLine($"<dt>Project</dt><dd>{Encode(r.ProjectName ?? "—")}</dd>");
            html.AppendLine($"<dt>Source file</dt><dd>{FormatLocationHtml(r.Location)}</dd>");
            if (r.Entrypoints.Count > 0)
                html.AppendLine($"<dt>Entrypoints</dt><dd>{string.Join(", ", r.Entrypoints.Select(e => $"<code>{Encode(e)}</code>"))}</dd>");
            if (r.Properties.Count > 0)
                html.AppendLine($"<dt>Payload</dt><dd>{string.Join(", ", r.Properties.Select(p => $"<code>{Encode(p.Name)}: {Encode(p.Type)}</code>"))}</dd>");
            if (r.TouchedEntities.Count > 0)
                html.AppendLine($"<dt>DbSets</dt><dd>{string.Join(", ", r.TouchedEntities.Select(e => $"<code>{Encode(e)}</code>"))}</dd>");
            if (r.PublishedNotifications.Count > 0)
                html.AppendLine($"<dt>Events</dt><dd>{string.Join(", ", r.PublishedNotifications.Select(n => $"<code>{Encode(CSharpScanner.SimpleName(n))}</code>"))}</dd>");
            html.AppendLine("</dl>");
            html.AppendLine("</div>");

            // Column 2: Linked Handlers
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
                    var hInfo = ResolveHandler(handlerName, handlersByFullName, handlersBySimpleName);
                    var hDisplay = CSharpScanner.SimpleName(handlerName);
                    var hLoc = hInfo?.Location is not null ? $" · {FormatLocationHtml(hInfo.Location)}" : "";



                    var hProj = !string.IsNullOrEmpty(hInfo?.ProjectName) ? $" <span class=\"meta-sub\">({Encode(hInfo!.ProjectName)})</span>" : "";
                    var hDeps = hInfo is { Dependencies.Count: > 0 } ? $"<br><span class=\"meta-sub\">Deps: <code>{Encode(string.Join(", ", hInfo.Dependencies))}</code></span>" : "";
                    html.AppendLine($"<li><strong>{Encode(hDisplay)}</strong>{hProj}<br><span class=\"meta-sub\"><code>{Encode(hInfo?.FullName ?? handlerName)}</code>{hLoc}</span>{hDeps}</li>");


                }
                html.AppendLine("</ul>");
            }
            html.AppendLine("</div>");

            // Column 3: Linked Validators
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
                    var vInfo = ResolveValidator(validatorName, validatorsByFullName, validatorsBySimpleName);
                    var vDisplay = CSharpScanner.SimpleName(validatorName);
                    var vLoc = vInfo?.Location is not null ? $" · {FormatLocationHtml(vInfo.Location)}" : "";
                    var vProj = !string.IsNullOrEmpty(vInfo?.ProjectName) ? $" <span class=\"meta-sub\">({Encode(vInfo!.ProjectName)})</span>" : "";
                    html.AppendLine($"<li><strong>{Encode(vDisplay)}</strong>{vProj}<br><span class=\"meta-sub\"><code>{Encode(vInfo?.FullName ?? validatorName)}</code>{vLoc}</span></li>");
                }
                html.AppendLine("</ul>");
            }
            html.AppendLine("</div>");

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
        html.AppendLine("</div>"); // .explorer-list
        html.AppendLine("</section>");
    }

    private static string BuildSearchBlob(
        CqrsRequest r,
        IReadOnlyDictionary<string, HandlerInfo> handlersByFull,
        IReadOnlyDictionary<string, HandlerInfo> handlersBySimple,
        IReadOnlyDictionary<string, ValidatorInfo> validatorsByFull,
        IReadOnlyDictionary<string, ValidatorInfo> validatorsBySimple)
    {
        var sb = new StringBuilder();
        sb.Append(r.FullName).Append(' ')
          .Append(r.Kind).Append(' ')
          .Append(r.ResponseType).Append(' ')
          .Append(r.ProjectName).Append(' ')
          .Append(r.Location?.File).Append(' ');

        foreach (var ep in r.Entrypoints)
            sb.Append(ep).Append(' ');
        foreach (var prop in r.Properties)
            sb.Append(prop.Name).Append(' ').Append(prop.Type).Append(' ');
        foreach (var entity in r.TouchedEntities)
            sb.Append(entity).Append(' ');
        foreach (var notif in r.PublishedNotifications)
            sb.Append(notif).Append(' ');

        foreach (var h in r.Handlers)
        {
            sb.Append(h).Append(' ');
            var info = ResolveHandler(h, handlersByFull, handlersBySimple);
            if (info?.Location is not null)
                sb.Append(info.Location.File).Append(' ');
        }

        foreach (var v in r.Validators)
        {
            sb.Append(v).Append(' ');
            var info = ResolveValidator(v, validatorsByFull, validatorsBySimple);
            if (info?.Location is not null)
                sb.Append(info.Location.File).Append(' ');
        }

        return sb.ToString().Trim();
    }

    private static HandlerInfo? ResolveHandler(
        string name,
        IReadOnlyDictionary<string, HandlerInfo> byFull,
        IReadOnlyDictionary<string, HandlerInfo> bySimple)
    {
        if (byFull.TryGetValue(name, out var h))
            return h;
        return bySimple.TryGetValue(CSharpScanner.SimpleName(name), out h) ? h : null;
    }

    private static ValidatorInfo? ResolveValidator(
        string name,
        IReadOnlyDictionary<string, ValidatorInfo> byFull,
        IReadOnlyDictionary<string, ValidatorInfo> bySimple)
    {
        if (byFull.TryGetValue(name, out var v))
            return v;
        return bySimple.TryGetValue(CSharpScanner.SimpleName(name), out v) ? v : null;
    }

    private static string FormatLocationHtml(SourceLocation? location)
        => location is null
            ? "—"
            : $"<code>{Encode($"{location.File}:{location.Line}:{location.Column}")}</code>";
}