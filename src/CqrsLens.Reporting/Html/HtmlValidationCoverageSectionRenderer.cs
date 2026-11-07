using System.Text;
using static CqrsLens.HtmlReportText;

namespace CqrsLens;

/// <summary>
/// Renders the validation-coverage figure: per project and solution-wide. Pure HTML,
/// so it renders identically in the interactive and zero-JS SVG reports.
/// </summary>
internal static class HtmlValidationCoverageSectionRenderer
{
    internal static void Append(StringBuilder html, AnalysisResult result)
    {
        var view = ValidationCoverageView.Build(result);

        html.AppendLine("<h2>Validation Coverage</h2>");
        html.AppendLine("<p class=\"note\">The share of requests with a linked validator (the model's " +
                        "request→validator linkage). A request with no linked validator is also reported " +
                        "individually by the command-without-validator check.</p>");
        html.AppendLine("<table><thead><tr><th>Project</th><th>Requests</th>" +
                        "<th>With linked validator</th><th>Coverage</th><th>Validators</th></tr></thead><tbody>");
        foreach (var row in view.Projects)
        {
            html.AppendLine($"<tr><td>{Encode(row.Project)}</td><td>{row.Requests}</td><td>{row.Covered}</td>" +
                            $"<td>{Encode(ValidationCoverageView.CoverageText(row))}</td>" +
                            $"<td>{row.Validators}</td></tr>");
        }
        var solution = view.Solution;
        html.AppendLine($"<tr><td><strong>{Encode(solution.Project)}</strong></td>" +
                        $"<td><strong>{solution.Requests}</strong></td>" +
                        $"<td><strong>{solution.Covered}</strong></td>" +
                        $"<td><strong>{Encode(ValidationCoverageView.CoverageText(solution))}</strong></td>" +
                        $"<td><strong>{solution.Validators}</strong></td></tr>");
        html.AppendLine("</tbody></table>");
    }
}
