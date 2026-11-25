using System.Text;
using static CqrsLens.HtmlReportText;
using static CqrsLens.MarkdownEscaping;

namespace CqrsLens;

/// <summary>Renders single or batch <see cref="SliceDesignEvaluation"/> reports as Markdown or HTML/SVG.</summary>
public static class SliceDesignReportGenerator
{
    public static string GenerateMarkdown(SliceDesignEvaluation evaluation)
    {
        ArgumentNullException.ThrowIfNull(evaluation);

        var sb = new StringBuilder();
        sb.AppendLine($"# Slice Design Verification — {CodeSpan(evaluation.Design.Request)}");
        sb.AppendLine();
        AppendSliceMarkdownSection(sb, evaluation, headingLevel: 2);
        sb.AppendLine($"> Scope: {MarkdownText(evaluation.Scope)}");
        return sb.ToString();
    }

    public static string GenerateMarkdown(
        string root,
        IReadOnlyList<(string File, SliceDesignEvaluation Evaluation)> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        var matched = results.Count(r => string.Equals(r.Evaluation.Status, SliceDesignEvaluator.Matched, StringComparison.Ordinal));
        var sb = new StringBuilder();
        sb.AppendLine("# Slice Design Batch Verification");
        sb.AppendLine();
        sb.AppendLine($"- **Directory**: {CodeSpan(root)}");
        sb.AppendLine($"- **Specs Matched**: {matched} / {results.Count}");
        sb.AppendLine();
        sb.AppendLine("| File | Request | Status | Checks Matched |");
        sb.AppendLine("| :--- | :--- | :--- | ---: |");
        foreach (var (file, eval) in results)
        {
            var checksOk = eval.Checks.Count(c => string.Equals(c.Status, SliceDesignEvaluator.Matched, StringComparison.Ordinal));
            sb.AppendLine($"| {CodeSpan(file)} | {CodeSpan(eval.Design.Request)} | `{InlineText(eval.Status)}` | {checksOk} / {eval.Checks.Count} |");
        }
        sb.AppendLine();

        foreach (var (file, eval) in results)
        {
            sb.AppendLine($"## {CodeSpan(file)} — {CodeSpan(eval.Design.Request)}");
            sb.AppendLine();
            AppendSliceMarkdownSection(sb, eval, headingLevel: 3);
        }

        return sb.ToString();
    }

    public static string GenerateHtml(
        SliceDesignEvaluation evaluation,
        DiagramRenderMode renderMode = DiagramRenderMode.Mermaid)
    {
        ArgumentNullException.ThrowIfNull(evaluation);

        return ModelingHtmlDocument.Render(
            $"Slice Design — {evaluation.Design.Request}",
            $"Project: {evaluation.Design.Project} · Status: {evaluation.Status}",
            renderMode,
            html =>
            {
                AppendSliceHtmlSection(html, evaluation, renderMode, headingLevel: 2);
                html.AppendLine($"<p class=\"note\">{Encode(evaluation.Scope)}</p>");
            });
    }

    public static string GenerateHtml(
        string root,
        IReadOnlyList<(string File, SliceDesignEvaluation Evaluation)> results,
        DiagramRenderMode renderMode = DiagramRenderMode.Mermaid)
    {
        ArgumentNullException.ThrowIfNull(results);

        var matched = results.Count(r => string.Equals(r.Evaluation.Status, SliceDesignEvaluator.Matched, StringComparison.Ordinal));
        var overall = new[]
        {
            SliceDesignEvaluator.Mismatch,
            SliceDesignEvaluator.Missing,
            SliceDesignEvaluator.NotDeterminable,
            SliceDesignEvaluator.Matched,
        }.FirstOrDefault(s => results.Any(r => string.Equals(r.Evaluation.Status, s, StringComparison.Ordinal)))
            ?? SliceDesignEvaluator.Matched;

        return ModelingHtmlDocument.Render(
            "Slice Design Batch Verification",
            $"Root: {root} · Overall: {overall} ({matched}/{results.Count} matched)",
            renderMode,
            html =>
            {
                html.AppendLine("<p>Overall Status: " + ModelingHtmlDocument.Badge(overall) + "</p>");
                html.AppendLine("<section class=\"cards\">");
                html.AppendLine($"<div class=\"card\"><div class=\"card-value\">{results.Count}</div><div class=\"card-label\">Total Specs</div></div>");
                html.AppendLine($"<div class=\"card\"><div class=\"card-value\">{matched}</div><div class=\"card-label\">Matched</div></div>");
                html.AppendLine($"<div class=\"card\"><div class=\"card-value\">{results.Count - matched}</div><div class=\"card-label\">Unmatched / Pending</div></div>");
                html.AppendLine("</section>");

                html.AppendLine("<h2>Specs Overview</h2>");
                html.AppendLine("<table><thead><tr><th>File</th><th>Request</th><th>Status</th><th>Checks Matched</th></tr></thead><tbody>");
                foreach (var (file, eval) in results)
                {
                    var ok = eval.Checks.Count(c => string.Equals(c.Status, SliceDesignEvaluator.Matched, StringComparison.Ordinal));
                    html.AppendLine(
                        $"<tr><td><code>{Encode(file)}</code></td><td><code>{Encode(eval.Design.Request)}</code></td>" +
                        $"<td>{ModelingHtmlDocument.Badge(eval.Status)}</td><td>{ok} / {eval.Checks.Count}</td></tr>");
                }
                html.AppendLine("</tbody></table>");

                foreach (var (file, eval) in results)
                {
                    html.AppendLine($"<h2>{Encode(file)} &mdash; <code>{Encode(eval.Design.Request)}</code></h2>");
                    AppendSliceHtmlSection(html, eval, renderMode, headingLevel: 3);
                }
            });
    }

    private static void AppendSliceMarkdownSection(
        StringBuilder sb,
        SliceDesignEvaluation evaluation,
        int headingLevel)
    {
        var sub = new string('#', headingLevel);
        sb.AppendLine($"- **Status**: `{InlineText(evaluation.Status)}` | **Project**: {CodeSpan(evaluation.Design.Project)} | **Kind**: `{InlineText(evaluation.Design.Kind)}` | **Response**: {CodeSpan(evaluation.Design.ResponseType ?? "void")}");
        sb.AppendLine();

        var diagram = SliceDesignDiagramBuilder.BuildMermaid(evaluation);
        sb.AppendLine($"{sub} Designed vs. Actual Blueprint");
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

        sb.AppendLine($"{sub} Checks");
        sb.AppendLine();
        sb.AppendLine("| Field | Status | Expected | Observed | Message |");
        sb.AppendLine("| :--- | :--- | :--- | :--- | :--- |");
        foreach (var check in evaluation.Checks)
        {
            var observed = check.Observed.Count == 0 ? "-" : string.Join(", ", check.Observed);
            sb.AppendLine($"| {CodeSpan(check.Field)} | `{InlineText(check.Status)}` | {TableCell(check.Expected)} | {TableCell(observed)} | {TableCell(check.Message)} |");
        }
        sb.AppendLine();

        if (evaluation.Design.Scenarios.Count > 0)
        {
            sb.AppendLine($"{sub} Acceptance Scenarios ({evaluation.Design.Scenarios.Count})");
            sb.AppendLine();
            foreach (var scenario in evaluation.Design.Scenarios)
            {
                sb.AppendLine($"- **{MarkdownText(scenario.Name)}**: Given {MarkdownText(scenario.Given)}; When {MarkdownText(scenario.When)}; Then {MarkdownText(scenario.Then)}.");
            }
            sb.AppendLine();
        }
    }

    private static void AppendSliceHtmlSection(
        StringBuilder html,
        SliceDesignEvaluation evaluation,
        DiagramRenderMode renderMode,
        int headingLevel)
    {
        html.AppendLine("<p>Status: " + ModelingHtmlDocument.Badge(evaluation.Status) +
                        $" · Project: <code>{Encode(evaluation.Design.Project)}</code> · Kind: <code>{Encode(evaluation.Design.Kind)}</code></p>");

        ModelingHtmlDocument.AppendDiagram(
            html,
            "Designed vs. Actual Blueprint",
            SliceDesignDiagramBuilder.BuildMermaid(evaluation),
            "No slice blueprint generated.",
            renderMode,
            headingLevel);

        html.AppendLine($"<h{headingLevel}>Checks</h{headingLevel}>");
        html.AppendLine("<table><thead><tr><th>Field</th><th>Status</th><th>Expected</th><th>Observed</th><th>Message</th></tr></thead><tbody>");
        foreach (var check in evaluation.Checks)
        {
            var observed = check.Observed.Count == 0 ? "-" : string.Join(", ", check.Observed);
            html.AppendLine(
                $"<tr><td><code>{Encode(check.Field)}</code></td><td>{ModelingHtmlDocument.Badge(check.Status)}</td>" +
                $"<td>{Encode(check.Expected)}</td><td>{Encode(observed)}</td><td>{Encode(check.Message)}</td></tr>");
        }
        html.AppendLine("</tbody></table>");

        if (evaluation.Design.Scenarios.Count > 0)
        {
            html.AppendLine($"<h{headingLevel}>Acceptance Scenarios ({evaluation.Design.Scenarios.Count})</h{headingLevel}><ul>");
            foreach (var s in evaluation.Design.Scenarios)
            {
                html.AppendLine(
                    $"<li><strong>{Encode(s.Name)}</strong>: Given {Encode(s.Given)}; " +
                    $"When {Encode(s.When)}; Then {Encode(s.Then)}.</li>");
            }
            html.AppendLine("</ul>");
        }
    }
}
