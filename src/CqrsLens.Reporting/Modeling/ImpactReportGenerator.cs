using System.Text;
using static CqrsLens.HtmlReportText;
using static CqrsLens.MarkdownEscaping;

namespace CqrsLens;

/// <summary>Renders <see cref="ChangeImpactReport"/> as visual Markdown or HTML/SVG.</summary>
public static class ImpactReportGenerator
{
    public static string GenerateMarkdown(ChangeImpactReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var targetTitle = report.Target is null
            ? "Unresolved Target"
            : $"{report.Target.Kind}: {report.Target.FullName ?? report.Target.Name}";

        var sb = new StringBuilder();
        sb.AppendLine($"# Change Impact Report — {CodeSpan(targetTitle)}");
        sb.AppendLine();
        sb.AppendLine($"- **Status**: `{InlineText(report.Status)}`");
        sb.AppendLine($"- **Confidence**: `{InlineText(report.Confidence)}`");
        sb.AppendLine($"- **Max Depth**: {report.MaxDepth}");
        sb.AppendLine($"- **Affected Projects**: {report.AffectedProjects.Count}");
        sb.AppendLine($"- **Impacted Items**: {report.ImpactedItems.Count}");
        sb.AppendLine($"- **Uncovered Hotspots**: {report.UncoveredHotspots.Count}");
        sb.AppendLine($"- **Existing Test References**: {report.ExistingTestReferences.Count}");
        sb.AppendLine($"- **Blind Spots**: {report.BlindSpots.Count}");
        sb.AppendLine();

        if (report.Candidates.Count > 0)
        {
            sb.AppendLine("## Disambiguation Candidates");
            sb.AppendLine();
            foreach (var c in report.Candidates)
                sb.AppendLine($"- `{InlineText(c.Kind)}` {CodeSpan(c.FullName ?? c.Name)}");
            sb.AppendLine();
        }

        var diagram = ImpactDiagramBuilder.BuildMermaid(report);
        if (diagram is not null)
        {
            sb.AppendLine("## Blast-Radius Diagram");
            sb.AppendLine();
            if (DiagramBuilder.IsWithinMermaidLimits(diagram))
            {
                sb.AppendLine("```mermaid");
                sb.AppendLine(diagram);
                sb.AppendLine("```");
            }
            else
            {
                sb.AppendLine("_Diagram omitted: exceeds Mermaid safety limits._");
            }
            sb.AppendLine();
        }

        if (report.UncoveredHotspots.Count > 0)
        {
            sb.AppendLine("## Uncovered Hotspots");
            sb.AppendLine();
            foreach (var hotspot in report.UncoveredHotspots)
                sb.AppendLine($"- {CodeSpan(hotspot)} (no matching test reference discovered)");
            sb.AppendLine();
        }

        sb.AppendLine("## Impacted Items");
        sb.AppendLine();
        if (report.ImpactedItems.Count == 0)
        {
            sb.AppendLine("_No impacted downstream or upstream items recorded._");
        }
        else
        {
            sb.AppendLine("| Direction | Depth | Kind | Target | Project | Reason |");
            sb.AppendLine("| :--- | ---: | :--- | :--- | :--- | :--- |");
            foreach (var item in report.ImpactedItems)
            {
                sb.AppendLine(
                    $"| {TableCell(item.Direction)} | {item.Depth} | `{TableCell(item.Kind)}` | " +
                    $"{CodeSpan(item.FullName)} | {TableCell(item.ProjectName ?? "-")} | {TableCell(item.Reason)} |");
            }
        }
        sb.AppendLine();

        if (report.ExistingTestReferences.Count > 0)
        {
            sb.AppendLine("## Existing Test References");
            sb.AppendLine();
            sb.AppendLine("| Project | Class.Method | Target | Reference Kind | Location |");
            sb.AppendLine("| :--- | :--- | :--- | :--- | :--- |");
            foreach (var testRef in report.ExistingTestReferences)
            {
                sb.AppendLine(
                    $"| {TableCell(testRef.ProjectName)} | {CodeSpan($"{testRef.ClassName}.{testRef.MethodName}")} | " +
                    $"{CodeSpan(testRef.TargetFullName)} | `{TableCell(testRef.ReferenceKind)}` | {CodeSpan($"{testRef.File}:{testRef.Line}")} |");
            }
            sb.AppendLine();
        }

        if (report.BlindSpots.Count > 0)
        {
            sb.AppendLine("## Static Analysis Blind Spots");
            sb.AppendLine();
            foreach (var b in report.BlindSpots)
                sb.AppendLine($"- `{InlineText(b.Code)}`: {MarkdownText(b.Message)}");
            sb.AppendLine();
        }

        sb.AppendLine($"> Scope: {MarkdownText(report.Scope)}");
        return sb.ToString();
    }

    public static string GenerateHtml(
        ChangeImpactReport report,
        DiagramRenderMode renderMode = DiagramRenderMode.Mermaid)
    {
        ArgumentNullException.ThrowIfNull(report);

        var targetTitle = report.Target is null
            ? "Unresolved Target"
            : $"{report.Target.Kind}: {report.Target.FullName ?? report.Target.Name}";

        var diagram = ImpactDiagramBuilder.BuildMermaid(report);
        return ModelingHtmlDocument.Render(
            $"Change Impact — {targetTitle}",
            $"Status: {report.Status} · Confidence: {report.Confidence} · Depth: {report.MaxDepth}",
            renderMode,
            html =>
            {
                html.AppendLine("<p>Status: " + ModelingHtmlDocument.Badge(report.Status) +
                                $" · Confidence: <strong>{Encode(report.Confidence)}</strong></p>");
                html.AppendLine("<section class=\"cards\">");
                AppendCard(html, "Affected Projects", report.AffectedProjects.Count);
                AppendCard(html, "Impacted Items", report.ImpactedItems.Count);
                AppendCard(html, "Uncovered Hotspots", report.UncoveredHotspots.Count);
                AppendCard(html, "Test References", report.ExistingTestReferences.Count);
                AppendCard(html, "Blind Spots", report.BlindSpots.Count);
                html.AppendLine("</section>");

                ModelingHtmlDocument.AppendDiagram(
                    html,
                    "Blast-Radius Diagram",
                    diagram,
                    "Target could not be uniquely resolved; inspect candidates below.",
                    renderMode);

                if (report.UncoveredHotspots.Count > 0)
                {
                    html.AppendLine("<div class=\"callout callout-err\"><strong>Uncovered Hotspots:</strong><ul>");
                    foreach (var hotspot in report.UncoveredHotspots)
                        html.AppendLine($"<li><code>{Encode(hotspot)}</code></li>");
                    html.AppendLine("</ul></div>");
                }

                if (report.Candidates.Count > 0)
                {
                    html.AppendLine("<h2>Candidates</h2><ul>");
                    foreach (var c in report.Candidates)
                        html.AppendLine($"<li><code>{Encode(c.Kind)}</code> <code>{Encode(c.FullName ?? c.Name)}</code></li>");
                    html.AppendLine("</ul>");
                }

                html.AppendLine("<h2>Impacted Items</h2>");
                if (report.ImpactedItems.Count == 0)
                {
                    html.AppendLine("<p><em>No impacted items recorded.</em></p>");
                }
                else
                {
                    html.AppendLine("<table><thead><tr><th>Direction</th><th>Depth</th><th>Kind</th><th>Target</th><th>Project</th><th>Reason</th></tr></thead><tbody>");
                    foreach (var item in report.ImpactedItems)
                    {
                        html.AppendLine(
                            $"<tr><td>{Encode(item.Direction)}</td><td>{item.Depth}</td><td><code>{Encode(item.Kind)}</code></td>" +
                            $"<td><code>{Encode(item.FullName)}</code></td><td>{Encode(item.ProjectName ?? "-")}</td><td>{Encode(item.Reason)}</td></tr>");
                    }
                    html.AppendLine("</tbody></table>");
                }

                if (report.ExistingTestReferences.Count > 0)
                {
                    html.AppendLine("<h2>Existing Test References</h2>");
                    html.AppendLine("<table><thead><tr><th>Project</th><th>Method</th><th>Target</th><th>Kind</th><th>Location</th></tr></thead><tbody>");
                    foreach (var testRef in report.ExistingTestReferences)
                    {
                        html.AppendLine(
                            $"<tr><td>{Encode(testRef.ProjectName)}</td><td><code>{Encode($"{testRef.ClassName}.{testRef.MethodName}")}</code></td>" +
                            $"<td><code>{Encode(testRef.TargetFullName)}</code></td><td><code>{Encode(testRef.ReferenceKind)}</code></td>" +
                            $"<td><code>{Encode($"{testRef.File}:{testRef.Line}")}</code></td></tr>");
                    }
                    html.AppendLine("</tbody></table>");
                }

                if (report.BlindSpots.Count > 0)
                {
                    html.AppendLine("<div class=\"callout callout-warn\"><strong>Static Blind Spots:</strong><ul>");
                    foreach (var b in report.BlindSpots)
                        html.AppendLine($"<li><code>{Encode(b.Code)}</code>: {Encode(b.Message)}</li>");
                    html.AppendLine("</ul></div>");
                }

                html.AppendLine($"<p class=\"note\">{Encode(report.Scope)}</p>");
            });
    }

    private static void AppendCard(StringBuilder html, string label, int value)
        => html.AppendLine($"<div class=\"card\"><div class=\"card-value\">{value}</div><div class=\"card-label\">{Encode(label)}</div></div>");
}
