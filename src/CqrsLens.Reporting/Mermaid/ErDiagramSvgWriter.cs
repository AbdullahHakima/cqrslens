using System.Text;

namespace CqrsLens;

/// <summary>Serializes a laid-out ER diagram to deterministic, overlap-free inline SVG.</summary>
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

        // Connector lines first, entities next, collision-free labels last.
        var curveData = WriteRelationshipLines(svg, model);
        WriteEntities(svg, model);
        WriteRelationshipLabels(svg, model, curveData, canvasWidth, canvasHeight);

        svg.Append("</svg></div>");
        return svg.ToString();
    }

    private static Dictionary<int, (double X1, double Y1, double C1X, double C1Y, double C2X, double C2Y, double X2, double Y2)>
        WriteRelationshipLines(StringBuilder svg, ErDiagramModel model)
    {
        var curves = new Dictionary<int, (double X1, double Y1, double C1X, double C1Y, double C2X, double C2Y, double X2, double Y2)>();

        for (var i = 0; i < model.Relationships.Count; i++)
        {
            var rel = model.Relationships[i];
            if (!model.EntityById.TryGetValue(rel.PrincipalId, out var principal) ||
                !model.EntityById.TryGetValue(rel.DependentId, out var dependent))
            {
                continue;
            }

            svg.Append(
                $"<g class=\"er-relationship\" data-principal=\"{E(rel.PrincipalId)}\" " +
                $"data-dependent=\"{E(rel.DependentId)}\" data-connector=\"{E(rel.Connector)}\">");

            if (string.Equals(rel.PrincipalId, rel.DependentId, StringComparison.Ordinal))
            {
                // Self-referential relationship: a small loop above the entity box.
                var loopLeftX = principal.X + principal.Width * 0.3;
                var loopRightX = principal.X + principal.Width * 0.7;
                var loopTopY = principal.Y - 28;
                svg.Append(
                    $"<path class=\"svg-er-line\" d=\"M {F(loopLeftX)} {F(principal.Y)} L {F(loopLeftX)} {F(loopTopY)} " +
                    $"L {F(loopRightX)} {F(loopTopY)} L {F(loopRightX)} {F(principal.Y)}\" " +
                    "fill=\"none\" stroke=\"#475569\" stroke-width=\"1.5\"/>");
            }
            else
            {
                ComputePerimeterAnchors(principal, dependent, out var x1, out var y1, out var c1x, out var c1y, out var c2x, out var c2y, out var x2, out var y2);
                curves[i] = (x1, y1, c1x, c1y, c2x, c2y, x2, y2);
                svg.Append(
                    $"<path class=\"svg-er-line\" d=\"M {F(x1)} {F(y1)} C {F(c1x)} {F(c1y)}, {F(c2x)} {F(c2y)}, {F(x2)} {F(y2)}\" " +
                    "fill=\"none\" stroke=\"#475569\" stroke-width=\"1.5\"/>");
            }

            svg.Append("</g>");
        }

        return curves;
    }

    private static void ComputePerimeterAnchors(
        ErEntityBox principal,
        ErEntityBox dependent,
        out double x1,
        out double y1,
        out double c1x,
        out double c1y,
        out double c2x,
        out double c2y,
        out double x2,
        out double y2)
    {
        var pCx = principal.X + principal.Width / 2.0;
        var pCy = principal.Y + principal.Height / 2.0;
        var dCx = dependent.X + dependent.Width / 2.0;
        var dCy = dependent.Y + dependent.Height / 2.0;

        var dx = dCx - pCx;
        var dy = dCy - pCy;

        if (Math.Abs(dy) <= Math.Abs(dx) * 0.75)
        {
            if (dx >= 0)
            {
                x1 = principal.X + principal.Width;
                x2 = dependent.X;
            }
            else
            {
                x1 = principal.X;
                x2 = dependent.X + dependent.Width;
            }
            y1 = pCy;
            y2 = dCy;
            var span = (x2 - x1) * 0.45;
            c1x = x1 + span;
            c1y = y1;
            c2x = x2 - span;
            c2y = y2;
        }
        else
        {
            if (dy >= 0)
            {
                y1 = principal.Y + principal.Height;
                y2 = dependent.Y;
            }
            else
            {
                y1 = principal.Y;
                y2 = dependent.Y + dependent.Height;
            }
            x1 = pCx;
            x2 = dCx;
            var span = (y2 - y1) * 0.45;
            c1x = x1;
            c1y = y1 + span;
            c2x = x2;
            c2y = y2 - span;
        }
    }

    private static void WriteRelationshipLabels(
        StringBuilder svg,
        ErDiagramModel model,
        IReadOnlyDictionary<int, (double X1, double Y1, double C1X, double C1Y, double C2X, double C2Y, double X2, double Y2)> curves,
        double canvasWidth,
        double canvasHeight)
    {
        var entityBoxes = model.Entities
            .Select(e => (X: e.X - 4.0, Y: e.Y - 4.0, W: e.Width + 8.0, H: e.Height + 8.0))
            .ToList();
        var placedPills = new List<(double X, double Y, double W, double H)>();

        for (var i = 0; i < model.Relationships.Count; i++)
        {
            var rel = model.Relationships[i];
            if (!model.EntityById.TryGetValue(rel.PrincipalId, out var principal) ||
                !model.EntityById.TryGetValue(rel.DependentId, out var dependent))
            {
                continue;
            }

            var displayLabel = $"{rel.Label} ({FormatCardinality(rel.Connector)})";
            var pillWidth = Math.Max(64.0, Math.Ceiling(displayLabel.Length * 6.8 + 16.0));
            const double pillHeight = 20.0;

            double px;
            double py;

            if (string.Equals(rel.PrincipalId, rel.DependentId, StringComparison.Ordinal))
            {
                var midX = principal.X + principal.Width / 2.0;
                var midY = principal.Y - 36.0;
                px = Math.Clamp(midX - pillWidth / 2.0, 4.0, Math.Max(4.0, canvasWidth - 4.0 - pillWidth));
                py = Math.Clamp(midY - pillHeight / 2.0, 4.0, Math.Max(4.0, canvasHeight - 4.0 - pillHeight));
                placedPills.Add((px, py, pillWidth, pillHeight));
            }
            else if (!curves.TryGetValue(i, out var c))
            {
                continue;
            }
            else
            {
                var placed = false;
                px = 4.0;
                py = 4.0;

                foreach (var dy in new[] { 0.0, -32.0, 32.0, -56.0, 56.0, -84.0, 84.0 })
                {
                    foreach (var t in new[] { 0.50, 0.35, 0.65, 0.25, 0.75 })
                    {
                        var u = 1.0 - t;
                        var bx = u * u * u * c.X1 + 3 * u * u * t * c.C1X + 3 * u * t * t * c.C2X + t * t * t * c.X2;
                        var by = u * u * u * c.Y1 + 3 * u * u * t * c.C1Y + 3 * u * t * t * c.C2Y + t * t * t * c.Y2;

                        var candX = Math.Clamp(bx - pillWidth / 2.0, 4.0, Math.Max(4.0, canvasWidth - 4.0 - pillWidth));
                        var candY = Math.Clamp(by + dy - pillHeight / 2.0, 4.0, Math.Max(4.0, canvasHeight - 4.0 - pillHeight));

                        if (!OverlapsAny(candX, candY, pillWidth, pillHeight, entityBoxes, 0.0) &&
                            !OverlapsAny(candX, candY, pillWidth, pillHeight, placedPills, 4.0))
                        {
                            px = candX;
                            py = candY;
                            placed = true;
                            break;
                        }
                    }
                    if (placed)
                        break;
                }

                if (!placed)
                {
                    var midBx = 0.125 * c.X1 + 0.375 * c.C1X + 0.375 * c.C2X + 0.125 * c.X2;
                    var midBy = 0.125 * c.Y1 + 0.375 * c.C1Y + 0.375 * c.C2Y + 0.125 * c.Y2;
                    px = Math.Clamp(midBx - pillWidth / 2.0, 4.0, Math.Max(4.0, canvasWidth - 4.0 - pillWidth));
                    py = Math.Clamp(midBy - pillHeight / 2.0, 4.0, Math.Max(4.0, canvasHeight - 4.0 - pillHeight));
                }

                placedPills.Add((px, py, pillWidth, pillHeight));
            }

            var textX = px + pillWidth / 2.0;
            var textY = py + pillHeight / 2.0 + 4.0;

            svg.Append(
                $"<g class=\"er-relationship-label\" data-principal=\"{E(rel.PrincipalId)}\" " +
                $"data-dependent=\"{E(rel.DependentId)}\">");
            svg.Append(
                $"<rect class=\"svg-er-pill\" x=\"{F(px)}\" y=\"{F(py)}\" " +
                $"width=\"{F(pillWidth)}\" height=\"{F(pillHeight)}\" rx=\"4\" " +
                "fill=\"#ffffff\" fill-opacity=\"0.96\" stroke=\"#cbd5e1\" stroke-width=\"1\"/>");
            svg.Append(
                $"<text class=\"svg-er-pill-text\" x=\"{F(textX)}\" y=\"{F(textY)}\" text-anchor=\"middle\" " +
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
                $"<rect class=\"svg-er-box\" x=\"{F(entity.X)}\" y=\"{F(entity.Y)}\" width=\"{F(entity.Width)}\" height=\"{F(entity.Height)}\" " +
                "rx=\"6\" fill=\"#ffffff\" stroke=\"#2563eb\" stroke-width=\"1.5\"/>");
            svg.Append(
                $"<rect class=\"svg-er-header\" x=\"{F(entity.X)}\" y=\"{F(entity.Y)}\" width=\"{F(entity.Width)}\" height=\"{F(ErDiagramLayout.HeaderHeight)}\" " +
                "rx=\"6\" fill=\"#eff6ff\" stroke=\"#2563eb\" stroke-width=\"1.5\"/>");
            svg.Append(
                $"<text class=\"svg-er-title\" x=\"{F(entity.X + entity.Width / 2.0)}\" y=\"{F(entity.Y + 21)}\" " +
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
                        $"<line class=\"svg-er-sep\" x1=\"{F(entity.X)}\" y1=\"{F(rowY)}\" x2=\"{F(entity.X + entity.Width)}\" y2=\"{F(rowY)}\" " +
                        "stroke=\"#e2e8f0\" stroke-width=\"1\"/>");
                }

                var textY = rowY + 16.0;
                svg.Append(
                    $"<text class=\"svg-er-attr\" x=\"{F(entity.X + 10)}\" y=\"{F(textY)}\" fill=\"#475569\" " +
                    "font-family=\"ui-monospace, SFMono-Regular, Menlo, Consolas, monospace\" " +
                    $"font-size=\"11\">{E(attr.Type)} {E(attr.Name)}</text>");

                if (!string.IsNullOrEmpty(attr.Keys))
                {
                    svg.Append(
                        $"<text class=\"svg-er-key\" x=\"{F(entity.X + entity.Width - 10)}\" y=\"{F(textY)}\" text-anchor=\"end\" " +
                        "fill=\"#b45309\" font-family=\"system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif\" " +
                        $"font-size=\"10\" font-weight=\"600\">{E(attr.Keys)}</text>");
                }
            }

            svg.Append("</g>");
        }
    }

    private static bool OverlapsAny(
        double x,
        double y,
        double w,
        double h,
        List<(double X, double Y, double W, double H)> boxes,
        double margin)
    {
        foreach (var b in boxes)
        {
            if (x - margin < b.X + b.W &&
                x + w + margin > b.X &&
                y - margin < b.Y + b.H &&
                y + h + margin > b.Y)
            {
                return true;
            }
        }
        return false;
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
