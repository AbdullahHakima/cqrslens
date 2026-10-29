using System.Text;
using static CqrsLens.HtmlReportText;

namespace CqrsLens;

/// <summary>Renders DbContext tables and their ER diagrams.</summary>
internal static class HtmlDbContextSectionRenderer
{
    internal static void Append(StringBuilder html, AnalysisResult result, DiagramRenderMode renderMode)
    {
        // ---- Data model ----------------------------------------------------------------
        html.AppendLine("<h2>Data Access (DbContexts)</h2>");
        if (result.DbContexts.Count == 0)
        {
            html.AppendLine("<p><em>No DbContext classes found.</em></p>");
        }
        else
        {
            var orderedContexts = result.DbContexts
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Namespace, StringComparer.Ordinal)
                .ToList();
            var contextNameCounts = orderedContexts
                .GroupBy(c => c.Name, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

            html.AppendLine("<table>");
            html.AppendLine("<thead><tr><th>DbContext</th><th>Project</th><th>DbSets (property → entity)</th></tr></thead>");
            html.AppendLine("<tbody>");
            foreach (var ctx in orderedContexts)
            {
                var sets = ctx.DbSets.Count > 0
                    ? string.Join(", ", ctx.DbSets.Select(s => $"{s.Property} → {s.Entity}"))
                    : "—";
                html.AppendLine($"<tr><td>{Encode(ctx.Name)}</td><td>{Encode(ctx.ProjectName ?? "—")}</td><td>{Encode(sets)}</td></tr>");
            }
            html.AppendLine("</tbody>");
            html.AppendLine("</table>");

            foreach (var ctx in orderedContexts)
            {
                var erDiagram = DiagramBuilder.ErDiagram(ctx);
                if (erDiagram is null) continue;

                var title = contextNameCounts[ctx.Name] > 1 && !string.IsNullOrEmpty(ctx.Namespace)
                    ? $"{ctx.Namespace}.{ctx.Name}"
                    : ctx.Name;
                html.AppendLine($"<h3>{Encode(title)} ER Diagram</h3>");
                if (!DiagramBuilder.IsWithinMermaidLimits(erDiagram))
                {
                    html.AppendLine("<p class=\"note\"><em>Diagram omitted: the source exceeds the CqrsLens " +
                                    "safety limit (45,000 characters / 400 edges).</em></p>");
                    continue;
                }

                html.AppendLine("<figure class=\"diagram\">");
                html.AppendLine(HtmlDiagramSectionRenderer.Render(erDiagram, renderMode));
                html.AppendLine("</figure>");
            }
        }
    }
}