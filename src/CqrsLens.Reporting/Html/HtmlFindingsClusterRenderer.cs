using System.Globalization;
using System.Text;
using static CqrsLens.HtmlReportText;

namespace CqrsLens;

/// <summary>
/// Renders the findings cluster visual: aggregate counts by rule and module, and a
/// module × feature × rule matrix. Pure HTML/CSS, so it renders identically in both
/// the interactive (mermaid) and the zero-JS SVG reports.
/// </summary>
internal static class HtmlFindingsClusterRenderer
{
    internal static void Append(StringBuilder html, FindingsClustering.FindingsMetrics metrics)
    {
        html.AppendLine("<h3>Findings by module and feature</h3>");
        html.AppendLine("<p class=\"note\">Each row is one module (project) and feature cluster; " +
                        "each column is a rule. Every finding is counted exactly once; findings whose target " +
                        "has no resolvable feature appear under <code>(unassigned)</code>.</p>");
        html.AppendLine($"<p><strong>{metrics.Total}</strong> finding(s): " +
                        $"{metrics.Errors} error(s), {metrics.Warnings} warning(s) across " +
                        $"{metrics.ByModule.Count} module(s) and {metrics.Clusters.Count} cluster(s).</p>");

        var rules = metrics.ByRule.Select(r => r.Code).ToList();
        var maxTotal = metrics.Clusters.Count == 0 ? 0 : metrics.Clusters.Max(c => c.Total);

        html.AppendLine("<table class=\"findings-matrix\">");
        html.Append("<thead><tr><th>Module</th><th>Feature</th>");
        foreach (var rule in rules)
            html.Append($"<th>{Encode(rule)}</th>");
        html.AppendLine("<th>Errors</th><th>Warnings</th><th>Total</th></tr></thead>");
        html.AppendLine("<tbody>");
        foreach (var cluster in metrics.Clusters)
        {
            html.Append($"<tr><td>{Encode(cluster.Module)}</td><td>{Encode(cluster.Feature)}</td>");
            foreach (var rule in rules)
            {
                var match = cluster.Rules.FirstOrDefault(r => string.Equals(r.Code, rule, StringComparison.Ordinal));
                html.Append($"<td>{match?.Total ?? 0}</td>");
            }
            var pct = maxTotal == 0 ? 0 : (int)Math.Round(cluster.Total * 100.0 / maxTotal);
            html.AppendLine(
                $"<td>{cluster.Errors}</td><td>{cluster.Warnings}</td>" +
                $"<td>{cluster.Total} <span class=\"bar\" style=\"display:inline-block;height:8px;" +
                $"width:{pct.ToString(CultureInfo.InvariantCulture)}px;max-width:120px;background:#c7d2fe;" +
                $"vertical-align:middle\"></span></td></tr>");
        }
        html.AppendLine("</tbody></table>");

        html.AppendLine("<h4>Findings by module</h4>");
        html.AppendLine("<table><thead><tr><th>Module</th><th>Errors</th><th>Warnings</th><th>Total</th></tr></thead>");
        html.AppendLine("<tbody>");
        foreach (var module in metrics.ByModule)
            html.AppendLine(
                $"<tr><td>{Encode(module.Module)}</td><td>{module.Errors}</td>" +
                $"<td>{module.Warnings}</td><td>{module.Total}</td></tr>");
        html.AppendLine("</tbody></table>");
    }
}
