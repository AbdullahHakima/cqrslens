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
        => AppendCqrs(html, result, renderMode, CqrsMapSplitMode.Count);

    internal static void AppendCqrs(
        StringBuilder html,
        AnalysisResult result,
        DiagramRenderMode renderMode,
        CqrsMapSplitMode cqrsMapSplit)
    {
        html.AppendLine("<h2>CQRS Map</h2>");
        var sections = DiagramBuilder.CqrsMapSections(result, cqrsMapSplit);
        if (sections.Count == 0)
        {
            html.AppendLine("<p><em>No MediatR requests found.</em></p>");
            return;
        }

        AppendCqrsLegend(html, result);
        html.AppendLine($"<p class=\"note\">{CqrsGroupingNote(cqrsMapSplit)}</p>");

        if (sections.Count > 1)
        {
            var projectCount = result.Requests
                .Select(r => string.IsNullOrEmpty(r.ProjectName) ? "(unassigned)" : r.ProjectName)
                .Distinct(StringComparer.Ordinal)
                .Count();
            var projectText = projectCount == 1 ? "1 project" : $"{projectCount} projects";
            html.AppendLine($"<p class=\"note\">{result.Requests.Count} requests across {projectText}. " +
                            CqrsSplitNote(cqrsMapSplit) + "</p>");
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

    internal static void AppendFlow(
        StringBuilder html,
        AnalysisResult result,
        DiagramRenderMode renderMode)
    {
        var sections = DiagramBuilder.FlowDiagramSections(result);
        if (sections.Count == 0)
            return;

        html.AppendLine("<h2>Flow</h2>");
        var totalEdges = sections.Sum(s => s.Edges.Count + s.ResolvedEdges.Count);
        var edgeText = totalEdges == 1 ? "1 resolved flow edge" : $"{totalEdges} resolved flow edges";
        var sectionText = sections.Count == 1 ? "1 diagram" : $"{sections.Count} feature/project diagrams";
        var unresolvedNote = result.UnresolvedDispatches.Count > 0
            ? $" {result.UnresolvedDispatches.Count} unresolved dispatch call(s) are not drawn as edges."
            : "";
        var dbContextNote = result.ResolvedEdges.Count > 0
            ? "DbContext nodes appear when a handler references a discovered DbContext type directly, " +
              "or through an injected interface resolved through DI"
            : "DbContext nodes appear only when a handler directly references a discovered DbContext type";
        html.AppendLine(
            $"<p class=\"note\">{Encode(edgeText)} across {Encode(sectionText)} " +
            $"(endpoint → request → handler and onward; {Encode(dbContextNote)}; " +
            $"every flow edge appears in exactly one diagram).{Encode(unresolvedNote)}</p>");
        AppendFlowLegend(html, result.ResolvedEdges.Count > 0);

        foreach (var section in sections)
        {
            if (sections.Count > 1 || section.Label is not null)
                html.AppendLine($"<h3>{Encode(section.Heading)}</h3>");

            if (!DiagramBuilder.IsWithinMermaidLimits(section.Source))
            {
                html.AppendLine($"<p class=\"note\"><em>Diagram omitted: {section.Edges.Count} edges exceed " +
                                "the CqrsLens safety limit (45,000 characters / 400 edges).</em></p>");
                continue;
            }

            html.AppendLine("<figure class=\"diagram\">");
            html.AppendLine(Render(section.Source, renderMode));
            html.AppendLine($"<figcaption>{Encode(section.Caption)}</figcaption>");
            html.AppendLine("</figure>");
        }
    }

    private static void AppendFlowLegend(StringBuilder html, bool hasResolvedEdges)
    {
        html.AppendLine("<ul class=\"legend\" aria-label=\"Flow map legend\">");
        html.AppendLine("<li><span class=\"swatch\" style=\"background:#e0f2fe;border-color:#0284c7\" aria-hidden=\"true\"></span>Endpoint</li>");
        html.AppendLine("<li><span class=\"swatch\" style=\"background:#dbeafe;border-color:#2563eb\" aria-hidden=\"true\"></span>Request</li>");
        html.AppendLine("<li><span class=\"swatch handler\" aria-hidden=\"true\"></span>Handler</li>");
        html.AppendLine("<li><span class=\"swatch\" style=\"background:#fdf4ff;border-color:#a21caf\" aria-hidden=\"true\"></span>Notification</li>");
        html.AppendLine("<li><span class=\"swatch\" style=\"background:#ecfdf5;border-color:#059669\" aria-hidden=\"true\"></span>DbContext</li>");
        if (hasResolvedEdges)
            html.AppendLine("<li><span class=\"swatch\" style=\"background:#ffffff;border-color:#7c3aed;border-style:dashed\" aria-hidden=\"true\"></span>DI-resolved (dotted)</li>");
        html.AppendLine("</ul>");
    }

    private static string CqrsGroupingNote(CqrsMapSplitMode split)
        => split == CqrsMapSplitMode.Feature
            ? "Requests are grouped by feature. Handler and validator nodes are shown " +
              "where linked and may be declared in a different project."
            : "Requests are grouped by declaring project. Handler and validator nodes are shown " +
              "where linked and may be declared in a different project.";

    private static string CqrsSplitNote(CqrsMapSplitMode split) => split switch
    {
        CqrsMapSplitMode.Feature =>
            "Diagrams are split by feature; every request appears exactly once.",
        CqrsMapSplitMode.Unsplit =>
            "Diagrams are grouped by project and only split further when a map exceeds GitHub's " +
            "rendering limits; every request appears exactly once.",
        _ =>
            "Diagrams are split by project and balanced so each stays within GitHub's " +
            "rendering limits; every request appears exactly once.",
    };

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