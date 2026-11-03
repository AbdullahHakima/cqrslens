using System.Text;
using static CqrsLens.HtmlReportText;

namespace CqrsLens;

/// <summary>Renders the per-request sequence diagram section.</summary>
internal static class HtmlSequenceSectionRenderer
{
    internal static void Append(
        StringBuilder html,
        AnalysisResult result,
        DiagramRenderMode renderMode)
    {
        var sequences = RequestSequenceBuilder.Build(result);
        if (sequences.Count == 0)
            return;

        html.AppendLine("<h2>Request sequences</h2>");
        html.AppendLine("<p class=\"note\">One sequence diagram per request with a resolved handler, built " +
                        "only from data the scan model holds. Participants: endpoint → mediator → pipeline " +
                        "behaviors (registered order) → handler → DbContext → published notifications. A " +
                        "DbContext participant appears only when the model holds a handler → DbContext edge; " +
                        "behavior order is registered order, not execution order.</p>");

        foreach (var sequence in sequences)
        {
            html.AppendLine($"<h3>{Encode(sequence.RequestName)}</h3>");
            if (!DiagramBuilder.IsWithinMermaidLimits(sequence.Source))
            {
                html.AppendLine("<p class=\"note\"><em>Diagram omitted: the request sequence exceeds the " +
                                "CqrsLens safety limit (45,000 characters / 400 edges).</em></p>");
                continue;
            }

            html.AppendLine("<figure class=\"diagram\">");
            html.AppendLine(HtmlDiagramSectionRenderer.Render(sequence.Source, renderMode));
            html.AppendLine($"<figcaption>{Encode(sequence.Caption)}</figcaption>");
            html.AppendLine("</figure>");
        }
    }
}
