using System.Text;
using static CqrsLens.HtmlReportText;

namespace CqrsLens;

/// <summary>Renders architecture, CQRS and layer diagrams and their legends.</summary>
internal static class HtmlDiagramSectionRenderer
{
    internal static void Append(
        StringBuilder html,
        string title,
        string? diagram,
        string emptyText,
        DiagramRenderMode renderMode)
    {
        html.AppendLine($"<h2>{Encode(title)}</h2>");
        if (diagram is null)
        {
            html.AppendLine($"<p><em>{Encode(emptyText)}</em></p>");
            return;
        }

        if (!DiagramBuilder.IsWithinMermaidLimits(diagram))
        {
            html.AppendLine("<p class=\"note\"><em>Diagram omitted: the source exceeds the CqrsLens " +
                            "safety limit (45,000 characters / 400 edges).</em></p>");
            return;
        }

        html.AppendLine("<figure class=\"diagram\">");
        html.AppendLine(Render(diagram, renderMode));
        html.AppendLine("</figure>");
    }

    internal static void AppendCqrs(
        StringBuilder html,
        AnalysisResult result,
        DiagramRenderMode renderMode)
    {
        html.AppendLine("<h2>CQRS Map</h2>");
        var sections = DiagramBuilder.CqrsMapSections(result);
        if (sections.Count == 0)
        {
            html.AppendLine("<p><em>No MediatR requests found.</em></p>");
            return;
        }

        AppendCqrsLegend(html, result);
        html.AppendLine("<p class=\"note\">Requests are grouped by declaring project. Handler and validator " +
                        "nodes are shown where linked and may be declared in a different project.</p>");

        if (sections.Count > 1)
        {
            var projectCount = result.Requests
                .Select(r => string.IsNullOrEmpty(r.ProjectName) ? "(unassigned)" : r.ProjectName)
                .Distinct(StringComparer.Ordinal)
                .Count();
            var projectText = projectCount == 1 ? "1 project" : $"{projectCount} projects";
            html.AppendLine($"<p class=\"note\">{result.Requests.Count} requests across {projectText}. " +
                            "Diagrams are split by project and balanced so each stays within GitHub's " +
                            "rendering limits; every request appears exactly once.</p>");
        }

        foreach (var section in sections)
        {
            if (sections.Count > 1)
                html.AppendLine($"<h3>{Encode(section.Heading)}</h3>");

            if (!DiagramBuilder.IsWithinMermaidLimits(section.Source))
            {
                html.AppendLine($"<p class=\"note\"><em>Diagram omitted: {section.Requests.Count} requests exceed " +
                                "the CqrsLens safety limit (45,000 characters / 400 edges). The Requests table " +
                                "below lists every request.</em></p>");
                continue;
            }

            html.AppendLine("<figure class=\"diagram\">");
            html.AppendLine(Render(section.Source, renderMode));
            html.AppendLine($"<figcaption>{Encode(section.Caption)}</figcaption>");
            html.AppendLine("</figure>");
        }
    }

    private static void AppendCqrsLegend(StringBuilder html, AnalysisResult result)
    {
        html.AppendLine("<ul class=\"legend\" aria-label=\"CQRS map legend\">");
        AppendLegendItem(html, "command", "Command");
        AppendLegendItem(html, "query", "Query");
        AppendLegendItem(html, "request", "Other request");
        AppendLegendItem(html, "handler", "Handler");
        AppendLegendItem(html, "validator", "Validator");
        if (result.Checks.Any(c => c.Code == ArchitectureChecker.MissingHandlerCode))
            AppendLegendItem(html, "missing", "Missing handler");
        if (result.Checks.Any(c => c.Code == ArchitectureChecker.CommandWithoutValidatorCode))
            AppendLegendItem(html, "no-validator", "Command without validator");
        html.AppendLine("</ul>");
    }

    internal static void AppendLayerLegend(StringBuilder html, AnalysisResult result)
    {
        html.AppendLine("<ul class=\"legend\" aria-label=\"Layer colors\">");
        AppendLegendItem(html, "domain", "Domain");
        AppendLegendItem(html, "application", "Application");
        AppendLegendItem(html, "infrastructure", "Infrastructure");
        AppendLegendItem(html, "api", "Api / Web / UI");
        AppendLegendItem(html, "tests", "Tests / other");
        if (result.Checks.Any(c => c.Code == ArchitectureChecker.LayerViolationCode))
            AppendLegendItem(html, "violation", "Layer violation");
        html.AppendLine("</ul>");
    }

    private static void AppendLegendItem(StringBuilder html, string swatch, string label)
        => html.AppendLine(
            $"<li><span class=\"swatch {Encode(swatch)}\" aria-hidden=\"true\"></span>{Encode(label)}</li>");

    /// <summary>
    /// RENDERING SEAM: turns a Mermaid diagram source into its HTML representation.
    /// In <see cref="DiagramRenderMode.Mermaid"/> mode this emits a
    /// <c>&lt;pre class="mermaid"&gt;</c> block rendered in the browser by Mermaid.js.
    /// In <see cref="DiagramRenderMode.Svg"/> mode this compiles the diagram to
    /// deterministic inline SVG ahead of time for zero-JS HTML reports.
    /// </summary>
    internal static string Render(
        string mermaidCode,
        DiagramRenderMode renderMode = DiagramRenderMode.Mermaid)
        => renderMode == DiagramRenderMode.Svg
            ? MermaidSvgRenderer.Render(mermaidCode)
            : $"<pre class=\"mermaid\">{Encode(mermaidCode)}</pre>";
}