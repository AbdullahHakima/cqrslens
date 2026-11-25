using System.Text;
using static CqrsLens.HtmlReportText;

namespace CqrsLens;

/// <summary>
/// Renders zero-JS HTML accordion sections (<c>&lt;details class="portal-section"&gt;</c>)
/// for non-CQRS Direct Flows and Operation Sequences.
/// </summary>
internal static class HtmlDirectSectionRenderer
{
    internal static void AppendFlows(
        StringBuilder html,
        IReadOnlyList<DirectFlowDiagramSection> sections,
        DiagramRenderMode renderMode)
    {
        if (sections.Count == 0)
            return;

        html.AppendLine("<section id=\"direct-flows\">");
        html.AppendLine("<details class=\"portal-section\" open>");
        html.AppendLine($"<summary class=\"portal-summary\">Direct Flows ({sections.Count} project(s))</summary>");
        html.AppendLine("<div class=\"portal-body\">");
        html.AppendLine("<h2>Direct Flows</h2>");
        html.AppendLine("<p class=\"note\">Non-CQRS execution paths (HTTP Direct, gRPC, Background Workers, and Event Consumers) " +
                        "traced from entrypoints through injected services to DbContexts and published integration events.</p>");
        AppendFlowLegend(html);

        foreach (var section in sections)
        {
            html.AppendLine($"<h3>{Encode(section.Heading)}</h3>");
            if (!DiagramBuilder.IsWithinMermaidLimits(section.Source))
            {
                html.AppendLine("<p class=\"note\"><em>Diagram omitted: the direct flow exceeds the CqrsLens " +
                                "safety limit (45,000 characters / 400 edges).</em></p>");
                continue;
            }

            html.AppendLine("<figure class=\"diagram\">");
            html.AppendLine(HtmlDiagramSectionRenderer.Render(section.Source, renderMode));
            html.AppendLine($"<figcaption>{Encode(section.Caption)}</figcaption>");
            html.AppendLine("</figure>");
        }

        html.AppendLine("</div>");
        html.AppendLine("</details>");
        html.AppendLine("</section>");
    }

    internal static void AppendSequences(
        StringBuilder html,
        IReadOnlyList<DirectSequence> sequences,
        DiagramRenderMode renderMode,
        string heading = "Operation Sequences")
    {
        if (sequences.Count == 0)
            return;

        html.AppendLine("<section id=\"operation-sequences\">");
        html.AppendLine("<details class=\"portal-section\" open>");
        html.AppendLine($"<summary class=\"portal-summary\">{Encode(heading)} ({sequences.Count})</summary>");
        html.AppendLine("<div class=\"portal-body\">");
        html.AppendLine($"<h2>{Encode(heading)}</h2>");
        html.AppendLine("<p class=\"note\">Sequence diagrams for non-CQRS operations (direct HTTP endpoints, gRPC RPCs, workers, and event consumers) " +
                        "built strictly from traced service, DbContext, and integration-event dependencies.</p>");

        foreach (var seq in sequences)
        {
            html.AppendLine($"<h3>{Encode(seq.OperationName)}</h3>");
            if (!DiagramBuilder.IsWithinMermaidLimits(seq.Source))
            {
                html.AppendLine("<p class=\"note\"><em>Diagram omitted: the operation sequence exceeds the CqrsLens " +
                                "safety limit (45,000 characters / 400 edges).</em></p>");
                continue;
            }

            html.AppendLine("<figure class=\"diagram\">");
            html.AppendLine(HtmlDiagramSectionRenderer.Render(seq.Source, renderMode));
            html.AppendLine($"<figcaption>{Encode(seq.Caption)}</figcaption>");
            html.AppendLine("</figure>");
        }

        html.AppendLine("</div>");
        html.AppendLine("</details>");
        html.AppendLine("</section>");
    }

    private static void AppendFlowLegend(StringBuilder html)
    {
        html.AppendLine("<ul class=\"legend\" aria-label=\"Direct flow legend\">");
        html.AppendLine("<li><span class=\"swatch\" style=\"background:#e0f2fe;border-color:#0284c7\" aria-hidden=\"true\"></span>HTTP / UI</li>");
        html.AppendLine("<li><span class=\"swatch\" style=\"background:#eef2ff;border-color:#4f46e5\" aria-hidden=\"true\"></span>gRPC</li>");
        html.AppendLine("<li><span class=\"swatch\" style=\"background:#fff7ed;border-color:#c2410c\" aria-hidden=\"true\"></span>Worker</li>");
        html.AppendLine("<li><span class=\"swatch\" style=\"background:#fdf4ff;border-color:#a21caf\" aria-hidden=\"true\"></span>Consumer</li>");
        html.AppendLine("<li><span class=\"swatch handler\" aria-hidden=\"true\"></span>Service</li>");
        html.AppendLine("<li><span class=\"swatch\" style=\"background:#ecfdf5;border-color:#059669\" aria-hidden=\"true\"></span>DbContext</li>");
        html.AppendLine("<li><span class=\"swatch\" style=\"background:#fefce8;border-color:#ca8a04;border-style:dashed\" aria-hidden=\"true\"></span>Published event</li>");
        html.AppendLine("</ul>");
    }
}
