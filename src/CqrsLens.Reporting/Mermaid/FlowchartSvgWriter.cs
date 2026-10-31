using System.Text;

namespace CqrsLens;

/// <summary>Serializes a laid-out flowchart to deterministic inline SVG.</summary>
internal static class FlowchartSvgWriter
{
    private const double NodeHeight = 38.0;

    private static string E(string? value) => MermaidSvgText.Encode(value);

    private static string F(double value) => MermaidSvgText.Format(value);

    internal static string Write(FlowchartModel model, double canvasWidth, double canvasHeight)
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

        svg.Append("<defs>");
        svg.Append(
            "<marker id=\"arrow-default\" viewBox=\"0 0 10 10\" refX=\"9\" refY=\"5\" " +
            "markerWidth=\"6\" markerHeight=\"6\" orient=\"auto-start-reverse\">" +
            "<path d=\"M 0 1 L 10 5 L 0 9 z\" fill=\"#475569\"/></marker>");
        svg.Append(
            "<marker id=\"arrow-violation\" viewBox=\"0 0 10 10\" refX=\"9\" refY=\"5\" " +
            "markerWidth=\"6\" markerHeight=\"6\" orient=\"auto-start-reverse\">" +
            "<path d=\"M 0 1 L 10 5 L 0 9 z\" fill=\"#dc2626\"/></marker>");
        svg.Append("</defs>");

        WriteSubgraphs(svg, model);
        WriteEdges(svg, model);
        WriteNodes(svg, model);

        svg.Append("</svg></div>");
        return svg.ToString();
    }

    private static void WriteSubgraphs(StringBuilder svg, FlowchartModel model)
    {
        foreach (var sg in model.Subgraphs.Where(s => s.Nodes.Count > 0))
        {
            model.NodeStyles.TryGetValue(sg.Id, out var sgStyle);
            var stroke = sgStyle?.Stroke ?? "#94a3b8";
            var dash = sgStyle?.StrokeDasharray ?? "4 3";
            svg.Append($"<g class=\"subgraph\" data-id=\"{E(sg.Id)}\">");
            svg.Append(
                $"<rect x=\"{F(sg.X)}\" y=\"{F(sg.Y)}\" width=\"{F(sg.Width)}\" height=\"{F(sg.Height)}\" " +
                $"rx=\"8\" fill=\"none\" stroke=\"{E(stroke)}\" stroke-width=\"1.5\" stroke-dasharray=\"{E(dash)}\"/>");
            svg.Append(
                $"<text x=\"{F(sg.X + 12)}\" y=\"{F(sg.Y + 20)}\" fill=\"#475569\" " +
                $"font-family=\"system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif\" " +
                $"font-size=\"12\" font-weight=\"600\">{E(sg.Label)}</text>");
            svg.Append("</g>");
        }
    }

    private static void WriteEdges(StringBuilder svg, FlowchartModel model)
    {
        for (var i = 0; i < model.Edges.Count; i++)
        {
            var edge = model.Edges[i];
            if (!model.Nodes.TryGetValue(edge.SourceId, out var src) ||
                !model.Nodes.TryGetValue(edge.TargetId, out var dst))
                continue;

            model.LinkStyles.TryGetValue(i, out var linkStyle);
            var isViolation = linkStyle?.Stroke is "#dc2626";
            var stroke = linkStyle?.Stroke ?? "#475569";
            var strokeWidth = linkStyle?.StrokeWidth ?? "1.5";
            var dash = linkStyle?.StrokeDasharray ?? (edge.IsDotted ? "4 3" : null);
            var marker = isViolation ? "url(#arrow-violation)" : "url(#arrow-default)";

            GetEdgeEndpoints(src, dst, model.IsLeftToRight, out var x1, out var y1, out var x2, out var y2);
            var midX = (x1 + x2) / 2.0;
            var midY = (y1 + y2) / 2.0;

            string pathData;
            if (model.IsLeftToRight)
            {
                var c1X = x1 + (x2 - x1) * 0.45;
                var c2X = x1 + (x2 - x1) * 0.55;
                pathData = $"M {F(x1)} {F(y1)} C {F(c1X)} {F(y1)}, {F(c2X)} {F(y2)}, {F(x2)} {F(y2)}";
            }
            else
            {
                var c1Y = y1 + (y2 - y1) * 0.45;
                var c2Y = y1 + (y2 - y1) * 0.55;
                pathData = $"M {F(x1)} {F(y1)} C {F(x1)} {F(c1Y)}, {F(x2)} {F(c2Y)}, {F(x2)} {F(y2)}";
            }

            svg.Append($"<g class=\"edge\" data-source=\"{E(src.Id)}\" data-target=\"{E(dst.Id)}\">");
            svg.Append($"<path d=\"{pathData}\" fill=\"none\" stroke=\"{E(stroke)}\" stroke-width=\"{E(strokeWidth)}\"");
            if (!string.IsNullOrEmpty(dash))
                svg.Append($" stroke-dasharray=\"{E(dash)}\"");
            svg.Append($" marker-end=\"{marker}\"/>");

            if (!string.IsNullOrEmpty(edge.Label))
            {
                var pillWidth = Math.Max(48.0, Math.Ceiling(edge.Label.Length * 6.8 + 14.0));
                const double pillHeight = 18.0;
                var labelStroke = isViolation ? "#dc2626" : "#cbd5e1";
                var labelTextFill = isViolation ? "#991b1b" : "#334155";
                svg.Append(
                    $"<rect x=\"{F(midX - pillWidth / 2.0)}\" y=\"{F(midY - pillHeight / 2.0)}\" " +
                    $"width=\"{F(pillWidth)}\" height=\"{F(pillHeight)}\" rx=\"4\" " +
                    $"fill=\"#ffffff\" stroke=\"{labelStroke}\" stroke-width=\"1\"/>");
                svg.Append(
                    $"<text x=\"{F(midX)}\" y=\"{F(midY + 4)}\" text-anchor=\"middle\" " +
                    $"fill=\"{labelTextFill}\" font-family=\"system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif\" " +
                    $"font-size=\"11\">{E(edge.Label)}</text>");
            }

            svg.Append("</g>");
        }
    }

    private static void WriteNodes(StringBuilder svg, FlowchartModel model)
    {
        foreach (var node in model.NodeOrder)
        {
            StyleProps? style = null;
            if (node.ClassName is not null && model.ClassDefs.TryGetValue(node.ClassName, out var clsStyle))
                style = clsStyle;
            if (model.NodeStyles.TryGetValue(node.Id, out var directStyle))
                style = directStyle;

            var fill = style?.Fill ?? "#ffffff";
            var stroke = style?.Stroke ?? "#475569";
            var strokeWidth = style?.StrokeWidth ?? "1.5";
            var dash = style?.StrokeDasharray;
            var textColor = style?.Color ?? "#1e293b";
            var rx = node.IsStadium ? NodeHeight / 2.0 : 6.0;

            svg.Append($"<g class=\"node\" data-id=\"{E(node.Id)}\">");
            svg.Append(
                $"<rect x=\"{F(node.X)}\" y=\"{F(node.Y)}\" width=\"{F(node.Width)}\" height=\"{F(node.Height)}\" " +
                $"rx=\"{F(rx)}\" fill=\"{E(fill)}\" stroke=\"{E(stroke)}\" stroke-width=\"{E(strokeWidth)}\"");
            if (!string.IsNullOrEmpty(dash))
                svg.Append($" stroke-dasharray=\"{E(dash)}\"");
            svg.Append("/>");

            svg.Append(
                $"<text x=\"{F(node.X + node.Width / 2.0)}\" y=\"{F(node.Y + node.Height / 2.0 + 4.5)}\" " +
                $"text-anchor=\"middle\" fill=\"{E(textColor)}\" " +
                $"font-family=\"system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif\" " +
                $"font-size=\"13\">{E(node.Label)}</text>");
            svg.Append("</g>");
        }
    }

    private static void GetEdgeEndpoints(
        FlowNode src,
        FlowNode dst,
        bool isLeftToRight,
        out double x1,
        out double y1,
        out double x2,
        out double y2)
    {
        var srcCx = src.X + src.Width / 2.0;
        var srcCy = src.Y + src.Height / 2.0;
        var dstCx = dst.X + dst.Width / 2.0;
        var dstCy = dst.Y + dst.Height / 2.0;

        if (isLeftToRight)
        {
            if (srcCx <= dstCx)
            {
                x1 = src.X + src.Width;
                y1 = srcCy;
                x2 = dst.X;
                y2 = dstCy;
            }
            else
            {
                x1 = src.X;
                y1 = srcCy;
                x2 = dst.X + dst.Width;
                y2 = dstCy;
            }
        }
        else
        {
            if (Math.Abs(srcCy - dstCy) < 1.0)
            {
                if (srcCx <= dstCx)
                {
                    x1 = src.X + src.Width;
                    y1 = srcCy;
                    x2 = dst.X;
                    y2 = dstCy;
                }
                else
                {
                    x1 = src.X;
                    y1 = srcCy;
                    x2 = dst.X + dst.Width;
                    y2 = dstCy;
                }
            }
            else if (srcCy < dstCy)
            {
                x1 = srcCx;
                y1 = src.Y + src.Height;
                x2 = dstCx;
                y2 = dst.Y;
            }
            else
            {
                x1 = srcCx;
                y1 = src.Y;
                x2 = dstCx;
                y2 = dst.Y + dst.Height;
            }
        }
    }
}
