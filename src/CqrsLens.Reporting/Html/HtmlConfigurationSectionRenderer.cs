using System.Text;
using static CqrsLens.HtmlReportText;

namespace CqrsLens;

/// <summary>
/// Renders the declared-vs-actual configuration and conventions view. Pure HTML, so
/// it renders identically in the interactive and zero-JS SVG reports.
/// </summary>
internal static class HtmlConfigurationSectionRenderer
{
    internal static void Append(StringBuilder html, AnalysisResult result)
    {
        var view = ConfigurationView.Build(result);

        html.AppendLine("<h2>Configuration &amp; Conventions</h2>");
        html.AppendLine("<p class=\"note\">The settings this scan ran under (declared) and what it found " +
                        "under them (actual). Declared values are the resolved configuration; actual values " +
                        "are the scan outcome.</p>");

        html.AppendLine("<h3>Declared</h3>");
        html.AppendLine("<table><thead><tr><th>Setting</th><th>Value</th></tr></thead><tbody>");
        foreach (var row in view.Declared)
            html.AppendLine($"<tr><td>{Encode(row.Label)}</td><td>{Encode(row.Value)}</td></tr>");
        html.AppendLine("</tbody></table>");

        html.AppendLine("<h3>Actual</h3>");
        html.AppendLine("<table><thead><tr><th>Fact</th><th>Value</th></tr></thead><tbody>");
        html.AppendLine($"<tr><td>Layer-model status</td><td>{Encode(view.LayerStatus)}</td></tr>");
        html.AppendLine($"<tr><td>Layer checks</td><td>{Encode(view.LayerChecks)}</td></tr>");
        html.AppendLine($"<tr><td>Requests discovered</td><td>{view.RequestsDiscovered}</td></tr>");
        html.AppendLine($"<tr><td>Conventions detected</td><td>{Encode(view.Conventions)}</td></tr>");
        html.AppendLine("</tbody></table>");
    }
}
