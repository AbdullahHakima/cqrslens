using System.Globalization;
using System.Text;
using static CqrsLens.HtmlReportText;
using static CqrsLens.MarkdownEscaping;

namespace CqrsLens;

/// <summary>Renders <see cref="ArchitectureProposalEvaluation"/> as visual Markdown or HTML/SVG.</summary>
public static class ProposalReportGenerator
{
    public static string GenerateMarkdown(
        AnalysisResult current,
        ArchitectureProposalEvaluation evaluation)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(evaluation);

        var sb = new StringBuilder();
        sb.AppendLine("# Architecture Proposal Evaluation");
        sb.AppendLine();
        sb.AppendLine($"- **Status**: `{InlineText(evaluation.Status)}`");
        sb.AppendLine($"- **Existing projects**: {evaluation.ExistingProjectCount}");
        sb.AppendLine($"- **Proposed projects**: {evaluation.ProposedProjects.Count}");
        sb.AppendLine($"- **Proposed references**: {evaluation.ProposedReferences.Count}");
        sb.AppendLine($"- **Findings**: {evaluation.Findings.Count}");
        sb.AppendLine();

        var diagram = ProposalDiagramBuilder.BuildMermaid(current, evaluation);
        if (diagram is not null)
        {
            sb.AppendLine("## Dependency Diff Diagram");
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

        if (evaluation.ProposedProjects.Count > 0 || evaluation.ProposedReferences.Count > 0)
        {
            sb.AppendLine("## Proposed Changes");
            sb.AppendLine();
            foreach (var p in evaluation.ProposedProjects)
                sb.AppendLine($"- `+` Project {CodeSpan(p.Name)} (layer: {CodeSpan(p.Layer)})");
            foreach (var r in evaluation.ProposedReferences)
            {
                var symbol = string.Equals(r.Operation, "remove", StringComparison.OrdinalIgnoreCase) ? "-" : "+";
                sb.AppendLine($"- `{symbol}` `{InlineText(r.Operation)}` reference {CodeSpan(r.From)} -> {CodeSpan(r.To)}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("## Findings");
        sb.AppendLine();
        if (evaluation.Findings.Count == 0)
        {
            sb.AppendLine("_No architecture violations or conflicts detected._");
        }
        else
        {
            sb.AppendLine("| Severity | Code | Message | Suggestion | Dependency Path |");
            sb.AppendLine("| :--- | :--- | :--- | :--- | :--- |");
            foreach (var f in evaluation.Findings)
            {
                var path = f.DependencyPath is { Count: > 0 }
                    ? string.Join(" -> ", f.DependencyPath)
                    : "-";
                sb.AppendLine($"| {TableCell(f.Severity)} | {CodeSpan(f.Code)} | {TableCell(f.Message)} | {TableCell(f.Suggestion)} | {TableCell(path)} |");
            }
        }
        sb.AppendLine();

        if (evaluation.Metrics is { Count: > 0 })
        {
            sb.AppendLine("## Coupling & Instability Metrics");
            sb.AppendLine();
            sb.AppendLine("| Project | Layer | Ca (Before -> After) | Ce (Before -> After) | Instability I |");
            sb.AppendLine("| :--- | :--- | :--- | :--- | :--- |");
            foreach (var m in evaluation.Metrics)
            {
                var delta = m.InstabilityAfter - m.InstabilityBefore;
                var deltaStr = delta.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture);
                sb.AppendLine(
                    $"| {CodeSpan(m.Project)} | {TableCell(m.Layer)} | " +
                    $"{m.AfferentBefore} -> {m.AfferentAfter} | " +
                    $"{m.EfferentBefore} -> {m.EfferentAfter} | " +
                    $"{m.InstabilityBefore.ToString("0.00", CultureInfo.InvariantCulture)} -> " +
                    $"{m.InstabilityAfter.ToString("0.00", CultureInfo.InvariantCulture)} ({deltaStr}) |");
            }
            sb.AppendLine();
        }

        if (evaluation.BlastRadius is { } blast)
        {
            sb.AppendLine("## Blast Radius");
            sb.AppendLine();
            sb.AppendLine($"- **Affected projects ({blast.AffectedProjects.Count})**: {(blast.AffectedProjects.Count == 0 ? "none" : string.Join(", ", blast.AffectedProjects.Select(CodeSpan)))}");
            sb.AppendLine($"- **Requests**: {blast.AffectedRequests} | **Handlers**: {blast.AffectedHandlers} | **Validators**: {blast.AffectedValidators} | **Endpoints**: {blast.AffectedEndpoints} | **DbContexts**: {blast.AffectedDbContexts}");
            if (blast.SampleRequests.Count > 0)
                sb.AppendLine($"- **Sample requests**: {string.Join(", ", blast.SampleRequests.Select(CodeSpan))}");
            sb.AppendLine();
        }

        sb.AppendLine($"> Scope: {MarkdownText(evaluation.Scope)}");
        return sb.ToString();
    }

    public static string GenerateHtml(
        AnalysisResult current,
        ArchitectureProposalEvaluation evaluation,
        DiagramRenderMode renderMode = DiagramRenderMode.Mermaid)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(evaluation);

        var diagram = ProposalDiagramBuilder.BuildMermaid(current, evaluation);
        return ModelingHtmlDocument.Render(
            "Architecture Proposal Evaluation",
            $"Status: {evaluation.Status} · Existing projects: {evaluation.ExistingProjectCount} · Findings: {evaluation.Findings.Count}",
            renderMode,
            html =>
            {
                html.AppendLine("<p>Status: " + ModelingHtmlDocument.Badge(evaluation.Status) + "</p>");
                html.AppendLine("<section class=\"cards\">");
                AppendCard(html, "Existing Projects", evaluation.ExistingProjectCount);
                AppendCard(html, "Proposed Projects", evaluation.ProposedProjects.Count);
                AppendCard(html, "Proposed References", evaluation.ProposedReferences.Count);
                AppendCard(html, "Findings", evaluation.Findings.Count);
                html.AppendLine("</section>");

                ModelingHtmlDocument.AppendDiagram(
                    html,
                    "Dependency Diff Diagram",
                    diagram,
                    "No projects in proposal.",
                    renderMode);

                html.AppendLine("<h2>Findings</h2>");
                if (evaluation.Findings.Count == 0)
                {
                    html.AppendLine("<div class=\"callout callout-ok\">No architecture violations or conflicts detected.</div>");
                }
                else
                {
                    html.AppendLine("<table><thead><tr><th>Severity</th><th>Code</th><th>Message</th><th>Suggestion</th><th>Path</th></tr></thead><tbody>");
                    foreach (var f in evaluation.Findings)
                    {
                        var path = f.DependencyPath is { Count: > 0 } ? string.Join(" -> ", f.DependencyPath) : "-";
                        html.AppendLine($"<tr><td>{Encode(f.Severity)}</td><td><code>{Encode(f.Code)}</code></td><td>{Encode(f.Message)}</td><td>{Encode(f.Suggestion)}</td><td><code>{Encode(path)}</code></td></tr>");
                    }
                    html.AppendLine("</tbody></table>");
                }

                if (evaluation.Metrics is { Count: > 0 })
                {
                    html.AppendLine("<h2>Coupling &amp; Instability Metrics</h2>");
                    html.AppendLine("<table><thead><tr><th>Project</th><th>Layer</th><th>Ca</th><th>Ce</th><th>Instability (I)</th></tr></thead><tbody>");
                    foreach (var m in evaluation.Metrics)
                    {
                        var delta = m.InstabilityAfter - m.InstabilityBefore;
                        var deltaStr = delta.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture);
                        var deltaCls = delta > 0.001 ? "delta-neg" : delta < -0.001 ? "delta-pos" : "";
                        html.AppendLine(
                            $"<tr><td><code>{Encode(m.Project)}</code></td><td>{Encode(m.Layer)}</td>" +
                            $"<td>{m.AfferentBefore} &rarr; {m.AfferentAfter}</td>" +
                            $"<td>{m.EfferentBefore} &rarr; {m.EfferentAfter}</td>" +
                            $"<td>{m.InstabilityBefore.ToString("0.00", CultureInfo.InvariantCulture)} &rarr; " +
                            $"{m.InstabilityAfter.ToString("0.00", CultureInfo.InvariantCulture)} <span class=\"{deltaCls}\">({Encode(deltaStr)})</span></td></tr>");
                    }
                    html.AppendLine("</tbody></table>");
                }

                if (evaluation.BlastRadius is { } blast)
                {
                    html.AppendLine("<h2>Blast Radius</h2>");
                    html.AppendLine("<section class=\"cards\">");
                    AppendCard(html, "Affected Projects", blast.AffectedProjects.Count);
                    AppendCard(html, "Requests", blast.AffectedRequests);
                    AppendCard(html, "Handlers", blast.AffectedHandlers);
                    AppendCard(html, "Validators", blast.AffectedValidators);
                    AppendCard(html, "Endpoints", blast.AffectedEndpoints);
                    AppendCard(html, "DbContexts", blast.AffectedDbContexts);
                    html.AppendLine("</section>");
                    if (blast.AffectedProjects.Count > 0)
                    {
                        html.AppendLine("<p>Affected projects: " +
                                        string.Join(", ", blast.AffectedProjects.Select(p => $"<code>{Encode(p)}</code>")) +
                                        "</p>");
                    }
                }

                html.AppendLine($"<p class=\"note\">{Encode(evaluation.Scope)}</p>");
            });
    }

    private static void AppendCard(StringBuilder html, string label, int value)
        => html.AppendLine($"<div class=\"card\"><div class=\"card-value\">{value}</div><div class=\"card-label\">{Encode(label)}</div></div>");
}
