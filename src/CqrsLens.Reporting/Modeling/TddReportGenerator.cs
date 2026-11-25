using System.Text;
using static CqrsLens.HtmlReportText;
using static CqrsLens.MarkdownEscaping;
using static CqrsLens.MermaidDiagramText;

namespace CqrsLens;

/// <summary>Renders single or batch <see cref="TddCycleEvaluation"/> reports as Markdown or HTML/SVG.</summary>
public static class TddReportGenerator
{
    public static string GenerateMarkdown(TddCycleEvaluation evaluation)
    {
        ArgumentNullException.ThrowIfNull(evaluation);

        var sb = new StringBuilder();
        sb.AppendLine("# Closed-Loop TDD Gate Report");
        sb.AppendLine();
        AppendSingleMarkdown(sb, evaluation, headingLevel: 2);
        sb.AppendLine($"> Scope: {MarkdownText(evaluation.Scope)}");
        return sb.ToString();
    }

    public static string GenerateMarkdown(TddBatchEvaluation batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        var sb = new StringBuilder();
        sb.AppendLine("# Closed-Loop TDD Batch Report");
        sb.AppendLine();
        sb.AppendLine($"- **Overall Phase**: `{InlineText(batch.Phase)}`");
        sb.AppendLine($"- **Root**: {CodeSpan(batch.Root)}");
        sb.AppendLine($"- **Verified Slices**: {batch.Verified} / {batch.Total}");
        sb.AppendLine();
        sb.AppendLine("| File | Phase | Design Status | Passed | Failed | Discovered Tests |");
        sb.AppendLine("| :--- | :--- | :--- | ---: | ---: | ---: |");
        foreach (var item in batch.Results)
        {
            var ev = item.Evaluation;
            sb.AppendLine(
                $"| {CodeSpan(item.File)} | `{InlineText(ev.Phase)}` | `{InlineText(ev.DesignStatus)}` | " +
                $"{ev.MatchingPassedTests.Count} | {ev.MatchingFailedTests.Count} | {ev.DiscoveredTests.Count} |");
        }
        sb.AppendLine();

        foreach (var item in batch.Results)
        {
            sb.AppendLine($"## {CodeSpan(item.File)} (`{InlineText(item.Evaluation.Phase)}`)");
            sb.AppendLine();
            AppendSingleMarkdown(sb, item.Evaluation, headingLevel: 3);
        }

        return sb.ToString();
    }

    public static string GenerateHtml(
        TddCycleEvaluation evaluation,
        DiagramRenderMode renderMode = DiagramRenderMode.Mermaid)
    {
        ArgumentNullException.ThrowIfNull(evaluation);

        return ModelingHtmlDocument.Render(
            "Closed-Loop TDD Gate Report",
            $"Phase: {evaluation.Phase} · Design: {evaluation.DesignStatus}",
            renderMode,
            html =>
            {
                AppendSingleHtml(html, evaluation, renderMode, headingLevel: 2);
                html.AppendLine($"<p class=\"note\">{Encode(evaluation.Scope)}</p>");
            });
    }

    public static string GenerateHtml(
        TddBatchEvaluation batch,
        DiagramRenderMode renderMode = DiagramRenderMode.Mermaid)
    {
        ArgumentNullException.ThrowIfNull(batch);

        return ModelingHtmlDocument.Render(
            "Closed-Loop TDD Batch Report",
            $"Root: {batch.Root} · Overall Phase: {batch.Phase} ({batch.Verified}/{batch.Total} verified)",
            renderMode,
            html =>
            {
                html.AppendLine("<p>Overall Phase: " + ModelingHtmlDocument.Badge(batch.Phase) + "</p>");
                html.AppendLine("<section class=\"cards\">");
                AppendCard(html, "Total Slices", batch.Total);
                AppendCard(html, "Verified", batch.Verified);
                AppendCard(html, "Remaining", batch.Total - batch.Verified);
                html.AppendLine("</section>");

                html.AppendLine("<h2>Slices Overview</h2>");
                html.AppendLine("<table><thead><tr><th>File</th><th>Phase</th><th>Design</th><th>Passed</th><th>Failed</th><th>Static Refs</th></tr></thead><tbody>");
                foreach (var item in batch.Results)
                {
                    var ev = item.Evaluation;
                    html.AppendLine(
                        $"<tr><td><code>{Encode(item.File)}</code></td><td>{ModelingHtmlDocument.Badge(ev.Phase)}</td>" +
                        $"<td>{ModelingHtmlDocument.Badge(ev.DesignStatus)}</td><td>{ev.MatchingPassedTests.Count}</td>" +
                        $"<td>{ev.MatchingFailedTests.Count}</td><td>{ev.DiscoveredTests.Count}</td></tr>");
                }
                html.AppendLine("</tbody></table>");

                foreach (var item in batch.Results)
                {
                    html.AppendLine($"<h2>{Encode(item.File)}</h2>");
                    AppendSingleHtml(html, item.Evaluation, renderMode, headingLevel: 3);
                }
            });
    }

    internal static string BuildPhaseDiagram(TddCycleEvaluation evaluation)
    {
        var ids = new MermaidIdAllocator();
        var specId = ids.Allocate("spec", "stg", "1. Spec Draft");
        var redId = ids.Allocate("red", "stg", "2. Red Tests");
        var greenId = ids.Allocate("green", "stg", "3. Code + Green");
        var verId = ids.Allocate("verified", "stg", "4. Verified");

        string ClassForStage(string stage)
        {
            if (evaluation.Phase == stage)
            {
                return stage switch
                {
                    "verified" => "clsTddOk",
                    "red" => "clsTddRed",
                    _ => "clsTddActive",
                };
            }

            if (evaluation.Phase is "drifted" or "inconclusive")
                return "clsTddWarn";

            return (evaluation.Phase, stage) switch
            {
                ("verified", _) => "clsTddOk",
                ("green_incomplete", "spec_draft" or "red") => "clsTddOk",
                ("red", "spec_draft") => "clsTddOk",
                _ => "clsTddPending",
            };
        }

        var sb = new StringBuilder();
        sb.AppendLine("flowchart LR");
        sb.AppendLine("    accTitle: TDD Cycle Phase");
        sb.AppendLine($"    accDescr: Current TDD cycle phase is {MermaidLabel(evaluation.Phase)}.");
        sb.AppendLine($"    {specId}([\"1. Spec ({MermaidLabel(evaluation.DesignStatus)})\"]):::{ClassForStage("spec_draft")}");
        sb.AppendLine($"    {redId}[\"2. Red (failed: {evaluation.MatchingFailedTests.Count})\"]:::{ClassForStage("red")}");
        sb.AppendLine($"    {greenId}[\"3. Green (passed: {evaluation.MatchingPassedTests.Count})\"]:::{ClassForStage("green_incomplete")}");
        sb.AppendLine($"    {verId}([\"4. Verified\"]):::{ClassForStage("verified")}");
        sb.AppendLine($"    {specId} --> {redId}");
        sb.AppendLine($"    {redId} --> {greenId}");
        sb.AppendLine($"    {greenId} --> {verId}");

        if (evaluation.Phase is "drifted" or "inconclusive")
        {
            var gateId = ids.Allocate("gate", "gate", $"Gate: {evaluation.Phase}");
            sb.AppendLine($"    {gateId}[\"{MermaidLabel($"Blocked: {evaluation.Phase}")}\"]:::clsTddRed");
            sb.AppendLine($"    {specId} -.->|blocked| {gateId}");
        }

        sb.AppendLine("    classDef clsTddOk fill:#dcfce7,stroke:#16a34a,stroke-width:2px,color:#14532d");
        sb.AppendLine("    classDef clsTddActive fill:#dbeafe,stroke:#2563eb,stroke-width:2px,color:#1e3a8a");
        sb.AppendLine("    classDef clsTddRed fill:#fee2e2,stroke:#dc2626,stroke-width:2px,color:#7f1d1d");
        sb.AppendLine("    classDef clsTddWarn fill:#fef3c7,stroke:#d97706,stroke-width:2px,color:#78350f");
        sb.AppendLine("    classDef clsTddPending fill:#f8fafc,stroke:#94a3b8,stroke-width:1.5px,color:#475569");
        return sb.ToString().TrimEnd();
    }

    private static void AppendSingleMarkdown(StringBuilder sb, TddCycleEvaluation ev, int headingLevel)
    {
        var sub = new string('#', headingLevel);
        sb.AppendLine($"- **Phase**: `{InlineText(ev.Phase)}` | **Design Status**: `{InlineText(ev.DesignStatus)}`");
        sb.AppendLine($"- **Static Test Refs**: {ev.DiscoveredTests.Count} | **Runtime TRX**: {(ev.HasRuntimeResults ? $"passed={ev.RuntimePassed}, failed={ev.RuntimeFailed}, skipped={ev.RuntimeSkipped}" : "not supplied")}");
        sb.AppendLine($"- **Matching Slice Tests**: passed={ev.MatchingPassedTests.Count}, failed={ev.MatchingFailedTests.Count}");
        sb.AppendLine();

        var diagram = BuildPhaseDiagram(ev);
        sb.AppendLine($"{sub} TDD Gate Pipeline");
        sb.AppendLine();
        sb.AppendLine("```mermaid");
        sb.AppendLine(diagram);
        sb.AppendLine("```");
        sb.AppendLine();

        if (ev.NextActions.Count > 0)
        {
            sb.AppendLine($"{sub} Next Actions");
            sb.AppendLine();
            foreach (var action in ev.NextActions)
                sb.AppendLine($"- [ ] {MarkdownText(action)}");
            sb.AppendLine();
        }

        if (ev.MatchingFailedTests.Count > 0 || ev.MatchingPassedTests.Count > 0)
        {
            sb.AppendLine($"{sub} Matching Runtime Tests");
            sb.AppendLine();
            foreach (var t in ev.MatchingFailedTests)
                sb.AppendLine($"- `FAILED` {CodeSpan(t)}");
            foreach (var t in ev.MatchingPassedTests)
                sb.AppendLine($"- `PASSED` {CodeSpan(t)}");
            sb.AppendLine();
        }

        if (ev.DesignChecks.Count > 0)
        {
            sb.AppendLine($"{sub} Design Checks");
            sb.AppendLine();
            sb.AppendLine("| Field | Status | Message |");
            sb.AppendLine("| :--- | :--- | :--- |");
            foreach (var check in ev.DesignChecks)
                sb.AppendLine($"| {CodeSpan(check.Field)} | `{InlineText(check.Status)}` | {TableCell(check.Message)} |");
            sb.AppendLine();
        }

        if (ev.SliceChecks.Count > 0)
        {
            sb.AppendLine($"{sub} Slice Architecture Findings");
            sb.AppendLine();
            foreach (var f in ev.SliceChecks)
                sb.AppendLine($"- `{f.Severity}` {CodeSpan(f.Code)}: {MarkdownText(f.Message)}");
            sb.AppendLine();
        }

        if (ev.DiscoveredTests.Count > 0)
        {
            sb.AppendLine($"{sub} Discovered Test References");
            sb.AppendLine();
            foreach (var tr in ev.DiscoveredTests)
                sb.AppendLine($"- {CodeSpan($"{tr.ClassName}.{tr.MethodName}")} (`{InlineText(tr.ReferenceKind)}`) at {CodeSpan($"{tr.File}:{tr.Line}")}");
            sb.AppendLine();
        }
    }

    private static void AppendSingleHtml(
        StringBuilder html,
        TddCycleEvaluation ev,
        DiagramRenderMode renderMode,
        int headingLevel)
    {
        html.AppendLine("<p>Phase: " + ModelingHtmlDocument.Badge(ev.Phase) +
                        " · Design: " + ModelingHtmlDocument.Badge(ev.DesignStatus) + "</p>");
        html.AppendLine("<section class=\"cards\">");
        AppendCard(html, "Static Test Refs", ev.DiscoveredTests.Count);
        AppendCard(html, "Matching Passed", ev.MatchingPassedTests.Count);
        AppendCard(html, "Matching Failed", ev.MatchingFailedTests.Count);
        AppendCard(html, "Architecture Findings", ev.SliceChecks.Count);
        html.AppendLine("</section>");

        ModelingHtmlDocument.AppendDiagram(
            html,
            "TDD Gate Pipeline",
            BuildPhaseDiagram(ev),
            "No pipeline rendered.",
            renderMode,
            headingLevel);

        if (ev.NextActions.Count > 0)
        {
            html.AppendLine($"<h{headingLevel}>Next Actions</h{headingLevel}><ul class=\"checklist\">");
            foreach (var a in ev.NextActions)
                html.AppendLine($"<li>{Encode(a)}</li>");
            html.AppendLine("</ul>");
        }

        if (ev.MatchingFailedTests.Count > 0 || ev.MatchingPassedTests.Count > 0)
        {
            html.AppendLine($"<h{headingLevel}>Matching Runtime Tests</h{headingLevel}><ul>");
            foreach (var t in ev.MatchingFailedTests)
                html.AppendLine($"<li><span class=\"badge badge-err\">FAILED</span> <code>{Encode(t)}</code></li>");
            foreach (var t in ev.MatchingPassedTests)
                html.AppendLine($"<li><span class=\"badge badge-ok\">PASSED</span> <code>{Encode(t)}</code></li>");
            html.AppendLine("</ul>");
        }

        if (ev.DesignChecks.Count > 0)
        {
            html.AppendLine($"<h{headingLevel}>Design Checks</h{headingLevel}>");
            html.AppendLine("<table><thead><tr><th>Field</th><th>Status</th><th>Message</th></tr></thead><tbody>");
            foreach (var c in ev.DesignChecks)
                html.AppendLine($"<tr><td><code>{Encode(c.Field)}</code></td><td>{ModelingHtmlDocument.Badge(c.Status)}</td><td>{Encode(c.Message)}</td></tr>");
            html.AppendLine("</tbody></table>");
        }

        if (ev.SliceChecks.Count > 0)
        {
            html.AppendLine($"<h{headingLevel}>Slice Architecture Findings</h{headingLevel}><ul>");
            foreach (var f in ev.SliceChecks)
                html.AppendLine($"<li><code>{Encode(f.Severity.ToString())}</code> <code>{Encode(f.Code)}</code>: {Encode(f.Message)}</li>");
            html.AppendLine("</ul>");
        }
    }

    private static void AppendCard(StringBuilder html, string label, int value)
        => html.AppendLine($"<div class=\"card\"><div class=\"card-value\">{value}</div><div class=\"card-label\">{Encode(label)}</div></div>");
}
