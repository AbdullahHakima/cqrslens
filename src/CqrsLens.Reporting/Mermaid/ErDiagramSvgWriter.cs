using System.Text;

namespace CqrsLens;

/// <summary>Serializes a laid-out ER diagram to deterministic inline SVG.</summary>
internal static class ErDiagramSvgWriter
{
    private static string E(string? value) => MermaidSvgText.Encode(value);

    private static string F(double value) => MermaidSvgText.Format(value);

    internal static string Write(ErDiagramModel model, double canvasWidth, double canvasHeight)
    {
        var svg = new StringBuilder();
        svg.Append("<div class=\"svg-diagram\">");
        svg.Append(
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {F(canvasWidth)} {F(canvasHeight)}\" " +
            $"width=\"{F(canvasWidth)}\" height=\"{F(canvasHeight)}\" role=\"img\">");

        if (!string.IsNullOrEmpty(model.AccTitle))
            svg.Append($"<title>{E(model.AccTitle)}</title>");
        if (!string.IsNullOrEmpty(model.AccDescr))
            svg.Append($"<desc>{E(model.AccDescr)}</desc>");

        WriteRelationships(svg, model);
        WriteEntities(svg, model);

        svg.Append("</svg></div>");
        return svg.ToString();
    }

    private static void WriteRelationships(StringBuilder svg, ErDiagramModel model)
    {
        foreach (var rel in model.Relationships)
        {
            if (!model.EntityById.TryGetValue(rel.PrincipalId, out var principal) ||
                !model.EntityById.TryGetValue(rel.DependentId, out var dependent))
                continue;

            var x1 = principal.X + principal.Width / 2.0;
            var y1 = principal.Y + principal.Height / 2.0;
            var x2 = dependent.X + dependent.Width / 2.0;
            var y2 = dependent.Y + dependent.Height / 2.0;
            var midX = (x1 + x2) / 2.0;
            var midY = (y1 + y2) / 2.0;

            svg.Append(
                $"<g class=\"er-relationship\" data-principal=\"{E(rel.PrincipalId)}\" " +
                $"data-dependent=\"{E(rel.DependentId)}\" data-connector=\"{E(rel.Connector)}\">");
            svg.Append(
                $"<path d=\"M {F(x1)} {F(y1)} L {F(x2)} {F(y2)}\" " +
                "fill=\"none\" stroke=\"#475569\" stroke-width=\"1.5\"/>");

            var displayLabel = $"{rel.Label} ({FormatCardinality(rel.Connector)})";
            var pillWidth = Math.Max(64.0, Math.Ceiling(displayLabel.Length * 6.6 + 16.0));
            const double pillHeight = 20.0;
            svg.Append(
                $"<rect x=\"{F(midX - pillWidth / 2.0)}\" y=\"{F(midY - pillHeight / 2.0)}\" " +
                $"width=\"{F(pillWidth)}\" height=\"{F(pillHeight)}\" rx=\"4\" " +
                "fill=\"#ffffff\" stroke=\"#cbd5e1\" stroke-width=\"1\"/>");
            svg.Append(
                $"<text x=\"{F(midX)}\" y=\"{F(midY + 4)}\" text-anchor=\"middle\" " +
                "fill=\"#334155\" font-family=\"system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif\" " +
                $"font-size=\"11\">{E(displayLabel)}</text>");
            svg.Append("</g>");
        }
    }

    private static void WriteEntities(StringBuilder svg, ErDiagramModel model)
    {
        foreach (var entity in model.Entities)
        {
            svg.Append($"<g class=\"er-entity\" data-id=\"{E(entity.Id)}\">");
            svg.Append(
                $"<rect x=\"{F(entity.X)}\" y=\"{F(entity.Y)}\" width=\"{F(entity.Width)}\" height=\"{F(entity.Height)}\" " +
                "rx=\"6\" fill=\"#ffffff\" stroke=\"#2563eb\" stroke-width=\"1.5\"/>");
            svg.Append(
                $"<rect x=\"{F(entity.X)}\" y=\"{F(entity.Y)}\" width=\"{F(entity.Width)}\" height=\"{F(ErDiagramLayout.HeaderHeight)}\" " +
                "rx=\"6\" fill=\"#eff6ff\" stroke=\"#2563eb\" stroke-width=\"1.5\"/>");
            svg.Append(
                $"<text x=\"{F(entity.X + entity.Width / 2.0)}\" y=\"{F(entity.Y + 21)}\" " +
                "text-anchor=\"middle\" fill=\"#1e3a8a\" " +
                "font-family=\"system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif\" " +
                $"font-size=\"13\" font-weight=\"600\">{E(entity.Label)}</text>");

            for (var r = 0; r < entity.Attributes.Count; r++)
            {
                var attr = entity.Attributes[r];
                var rowY = entity.Y + ErDiagramLayout.HeaderHeight + r * ErDiagramLayout.RowHeight;
                if (r > 0)
                {
                    svg.Append(
                        $"<line x1=\"{F(entity.X)}\" y1=\"{F(rowY)}\" x2=\"{F(entity.X + entity.Width)}\" y2=\"{F(rowY)}\" " +
                        "stroke=\"#e2e8f0\" stroke-width=\"1\"/>");
                }

                var textY = rowY + 16.0;
                svg.Append(
                    $"<text x=\"{F(entity.X + 10)}\" y=\"{F(textY)}\" fill=\"#475569\" " +
                    "font-family=\"ui-monospace, SFMono-Regular, Menlo, Consolas, monospace\" " +
                    $"font-size=\"11\">{E(attr.Type)} {E(attr.Name)}</text>");

                if (!string.IsNullOrEmpty(attr.Keys))
                {
                    svg.Append(
                        $"<text x=\"{F(entity.X + entity.Width - 10)}\" y=\"{F(textY)}\" text-anchor=\"end\" " +
                        "fill=\"#b45309\" font-family=\"system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif\" " +
                        $"font-size=\"10\" font-weight=\"600\">{E(attr.Keys)}</text>");
                }
            }

            svg.Append("</g>");
        }
    }

    private static string FormatCardinality(string connector)
        => connector switch
        {
            "||--o{" => "1..*",
            "|o--o{" => "0..1..*",
            "||--o|" => "1..0..1",
            "|o--o|" => "0..1..0..1",
            "}o--o{" => "*..*",
            _ => connector,
        };
}
