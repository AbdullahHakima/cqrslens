using System.Text;

namespace CqrsLens;

/// <summary>Serializes a laid-out flowchart to deterministic, collision-free inline SVG.</summary>
internal static class FlowchartSvgWriter
{
    private const double NodeBoxMargin = 4.0;
    private const double PillGap = 4.0;
    private const double CanvasPad = 6.0;

    private static readonly double[] CandidateTs = { 0.50, 0.36, 0.64, 0.25, 0.75, 0.18, 0.82 };
    private static readonly double[] CandidateDys = { 0.0, -24.0, 24.0, -44.0, 44.0, -64.0, 64.0 };

    private enum PortSide
    {
        Left,
        Right,
        Top,
        Bottom,
    }

    private sealed record PlannedEdge(
        int Index,
        FlowNode Source,
        FlowNode Target,
        FlowchartEdge Edge,
        StyleProps? LinkStyle,
        bool IsViolation,
        double X1,
        double Y1,
        double C1X,
        double C1Y,
        double C2X,
        double C2Y,
        double X2,
        double Y2,
        string PathData);

    private static string E(string? value) => MermaidSvgText.Encode(value);

    private static string F(double value) => MermaidSvgText.Format(value);

    internal static string Write(FlowchartModel model, double canvasWidth, double canvasHeight)
    {
        var planned = PlanEdges(model, canvasWidth, canvasHeight);
        var effectiveWidth = Math.Max(canvasWidth, ComputeRequiredMaxRight(planned, canvasWidth));
        var effectiveHeight = Math.Max(canvasHeight, ComputeRequiredMaxBottom(planned, canvasHeight));

        var svg = new StringBuilder();
        svg.Append("<div class=\"svg-diagram\">");
        svg.Append(
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {F(effectiveWidth)} {F(effectiveHeight)}\" " +
            $"width=\"{F(effectiveWidth)}\" height=\"{F(effectiveHeight)}\" role=\"img\">");

        if (!string.IsNullOrEmpty(model.AccTitle))
            svg.Append($"<title>{E(model.AccTitle)}</title>");
        if (!string.IsNullOrEmpty(model.AccDescr))
            svg.Append($"<desc>{E(model.AccDescr)}</desc>");

        svg.Append("<defs>");
        svg.Append(
            "<marker id=\"arrow-default\" viewBox=\"0 0 10 10\" refX=\"9\" refY=\"5\" " +
            "markerWidth=\"6\" markerHeight=\"6\" orient=\"auto-start-reverse\">" +
            "<path class=\"svg-arrow-default\" d=\"M 0 1 L 10 5 L 0 9 z\" fill=\"#475569\"/></marker>");
        svg.Append(
            "<marker id=\"arrow-violation\" viewBox=\"0 0 10 10\" refX=\"9\" refY=\"5\" " +
            "markerWidth=\"6\" markerHeight=\"6\" orient=\"auto-start-reverse\">" +
            "<path class=\"svg-arrow-violation\" d=\"M 0 1 L 10 5 L 0 9 z\" fill=\"#dc2626\"/></marker>");
        svg.Append("</defs>");

        // Paint order: Subgraphs -> Edge curves -> Nodes -> Collision-free Edge Label Pills.
        WriteSubgraphs(svg, model);
        WriteEdgePaths(svg, planned);
        WriteNodes(svg, model);
        WriteEdgeLabels(svg, model, planned, effectiveWidth, effectiveHeight);

        svg.Append("</svg></div>");
        return svg.ToString();
    }

    private static List<PlannedEdge> PlanEdges(FlowchartModel model, double canvasWidth, double canvasHeight)
    {
        var valid = new List<(int Index, FlowNode Src, FlowNode Dst, FlowchartEdge Edge, PortSide OutSide, PortSide InSide, bool SelfLoop)>();
        for (var i = 0; i < model.Edges.Count; i++)
        {
            var edge = model.Edges[i];
            if (!model.Nodes.TryGetValue(edge.SourceId, out var src) ||
                !model.Nodes.TryGetValue(edge.TargetId, out var dst))
            {
                continue;
            }

            if (string.Equals(src.Id, dst.Id, StringComparison.Ordinal))
            {
                valid.Add((i, src, dst, edge, PortSide.Top, PortSide.Top, true));
                continue;
            }

            DetermineSides(src, dst, model.IsLeftToRight, out var outSide, out var inSide);
            valid.Add((i, src, dst, edge, outSide, inSide, false));
        }

        // Distribute edge endpoints along the middle 65% of each node side sorted by counterpart coordinate.
        var outAnchors = new Dictionary<int, (double X, double Y)>();
        var inAnchors = new Dictionary<int, (double X, double Y)>();

        foreach (var group in valid.Where(v => !v.SelfLoop).GroupBy(v => (v.Src.Id, v.OutSide)))
        {
            var sorted = SortByCounterpart(group, isOutgoing: true, group.Key.OutSide);
            AssignPortPositions(sorted, group.Key.OutSide, isOutgoing: true, outAnchors);
        }

        foreach (var group in valid.Where(v => !v.SelfLoop).GroupBy(v => (v.Dst.Id, v.InSide)))
        {
            var sorted = SortByCounterpart(group, isOutgoing: false, group.Key.InSide);
            AssignPortPositions(sorted, group.Key.InSide, isOutgoing: false, inAnchors);
        }

        var planned = new List<PlannedEdge>(valid.Count);
        foreach (var item in valid)
        {
            model.LinkStyles.TryGetValue(item.Index, out var linkStyle);
            var isViolation = linkStyle?.Stroke is "#dc2626";

            if (item.SelfLoop)
            {
                var x1 = item.Src.X + item.Src.Width * 0.35;
                var x2 = item.Src.X + item.Src.Width * 0.65;
                var y1 = item.Src.Y;
                var y2 = item.Src.Y;
                var topY = Math.Max(8.0, item.Src.Y - 34.0);
                var path = $"M {F(x1)} {F(y1)} C {F(x1)} {F(topY)}, {F(x2)} {F(topY)}, {F(x2)} {F(y2)}";
                planned.Add(new PlannedEdge(item.Index, item.Src, item.Dst, item.Edge, linkStyle, isViolation,
                    x1, y1, x1, topY, x2, topY, x2, y2, path));
                continue;
            }

            var (p1x, p1y) = outAnchors[item.Index];
            var (p2x, p2y) = inAnchors[item.Index];
            BuildBezierControlPoints(
                model,
                item.Src,
                item.Dst,
                item.OutSide,
                item.InSide,
                p1x,
                p1y,
                p2x,
                p2y,
                canvasWidth,
                canvasHeight,
                out var c1x,
                out var c1y,
                out var c2x,
                out var c2y);

            var d = $"M {F(p1x)} {F(p1y)} C {F(c1x)} {F(c1y)}, {F(c2x)} {F(c2y)}, {F(p2x)} {F(p2y)}";
            planned.Add(new PlannedEdge(item.Index, item.Src, item.Dst, item.Edge, linkStyle, isViolation,
                p1x, p1y, c1x, c1y, c2x, c2y, p2x, p2y, d));
        }

        return planned;
    }

    private static List<(int Index, FlowNode Src, FlowNode Dst, FlowchartEdge Edge, PortSide OutSide, PortSide InSide, bool SelfLoop)>
        SortByCounterpart(
            IEnumerable<(int Index, FlowNode Src, FlowNode Dst, FlowchartEdge Edge, PortSide OutSide, PortSide InSide, bool SelfLoop)> group,
            bool isOutgoing,
            PortSide side)
    {
        return group.OrderBy(item =>
        {
            var other = isOutgoing ? item.Dst : item.Src;
            return side is PortSide.Left or PortSide.Right
                ? other.Y + other.Height / 2.0
                : other.X + other.Width / 2.0;
        }).ThenBy(item => item.Index).ToList();
    }

    private static void AssignPortPositions(
        List<(int Index, FlowNode Src, FlowNode Dst, FlowchartEdge Edge, PortSide OutSide, PortSide InSide, bool SelfLoop)> ordered,
        PortSide side,
        bool isOutgoing,
        Dictionary<int, (double X, double Y)> target)
    {
        var count = ordered.Count;
        for (var k = 0; k < count; k++)
        {
            var node = isOutgoing ? ordered[k].Src : ordered[k].Dst;
            var ratio = count == 1 ? 0.5 : 0.175 + 0.65 * k / (count - 1);
            (double X, double Y) pt = side switch
            {
                PortSide.Left => (node.X, node.Y + node.Height * ratio),
                PortSide.Right => (node.X + node.Width, node.Y + node.Height * ratio),
                PortSide.Top => (node.X + node.Width * ratio, node.Y),
                PortSide.Bottom => (node.X + node.Width * ratio, node.Y + node.Height),
                _ => (node.X + node.Width / 2.0, node.Y + node.Height / 2.0),
            };
            target[ordered[k].Index] = pt;
        }
    }

    private static void DetermineSides(
        FlowNode src,
        FlowNode dst,
        bool isLeftToRight,
        out PortSide outSide,
        out PortSide inSide)
    {
        var srcCx = src.X + src.Width / 2.0;
        var srcCy = src.Y + src.Height / 2.0;
        var dstCx = dst.X + dst.Width / 2.0;
        var dstCy = dst.Y + dst.Height / 2.0;

        if (isLeftToRight)
        {
            if (Math.Abs(srcCx - dstCx) < 40.0 && Math.Abs(srcCy - dstCy) > 1.0)
            {
                // Same-column vertical edge in LR flow: loop out on the right side cleanly.
                outSide = PortSide.Right;
                inSide = PortSide.Right;
            }
            else if (srcCx <= dstCx)
            {
                outSide = PortSide.Right;
                inSide = PortSide.Left;
            }
            else
            {
                outSide = PortSide.Left;
                inSide = PortSide.Right;
            }
        }
        else
        {
            if (Math.Abs(srcCy - dstCy) < 1.0)
            {
                outSide = srcCx <= dstCx ? PortSide.Right : PortSide.Left;
                inSide = srcCx <= dstCx ? PortSide.Left : PortSide.Right;
            }
            else if (srcCy < dstCy)
            {
                outSide = PortSide.Bottom;
                inSide = PortSide.Top;
            }
            else
            {
                outSide = PortSide.Top;
                inSide = PortSide.Bottom;
            }
        }
    }

    private static void BuildBezierControlPoints(
        FlowchartModel model,
        FlowNode src,
        FlowNode dst,
        PortSide outSide,
        PortSide inSide,
        double x1,
        double y1,
        double x2,
        double y2,
        double canvasWidth,
        double canvasHeight,
        out double c1x,
        out double c1y,
        out double c2x,
        out double c2y)
    {
        if (outSide == PortSide.Right && inSide == PortSide.Right)
        {
            var loopOut = Math.Clamp(Math.Abs(y2 - y1) * 0.35 + 24.0, 38.0, 88.0);
            c1x = x1 + loopOut;
            c1y = y1;
            c2x = x2 + loopOut;
            c2y = y2;
            return;
        }

        if (outSide is PortSide.Left or PortSide.Right && inSide is PortSide.Left or PortSide.Right)
        {
            var dx = (x2 - x1) * 0.45;
            c1x = x1 + dx;
            c1y = y1;
            c2x = x2 - dx;
            c2y = y2;

            // Long-span horizontal edge: check if midpoint hits an intermediate node box and arc gently clear.
            if (Math.Abs(x2 - x1) > 220.0)
            {
                var midX = (x1 + x2) / 2.0;
                var midY = (y1 + y2) / 2.0;
                if (HitsIntermediateNode(model, src.Id, dst.Id, midX - 24.0, midY - 12.0, 48.0, 24.0))
                {
                    foreach (var bend in new[] { -48.0, 48.0, -88.0, 88.0, -128.0, 128.0 })
                    {
                        var candMidY = midY + bend;
                        if (candMidY < 18.0 || candMidY > canvasHeight - 18.0)
                            continue;
                        if (!HitsIntermediateNode(model, src.Id, dst.Id, midX - 24.0, candMidY - 12.0, 48.0, 24.0))
                        {
                            c1y = y1 + bend * 0.85;
                            c2y = y2 + bend * 0.85;
                            break;
                        }
                    }
                }
            }
            return;
        }

        var dy = (y2 - y1) * 0.45;
        c1x = x1;
        c1y = y1 + dy;
        c2x = x2;
        c2y = y2 - dy;
        _ = canvasWidth;
    }

    private static bool HitsIntermediateNode(
        FlowchartModel model,
        string srcId,
        string dstId,
        double rx,
        double ry,
        double rw,
        double rh)
    {
        foreach (var node in model.NodeOrder)
        {
            if (string.Equals(node.Id, srcId, StringComparison.Ordinal) ||
                string.Equals(node.Id, dstId, StringComparison.Ordinal))
            {
                continue;
            }

            if (RectsOverlap(rx, ry, rw, rh, node.X - 2, node.Y - 2, node.Width + 4, node.Height + 4))
                return true;
        }

        return false;
    }

    private static double ComputeRequiredMaxRight(List<PlannedEdge> planned, double canvasWidth)
    {
        var maxX = canvasWidth;
        foreach (var e in planned)
        {
            maxX = Math.Max(maxX, e.C1X + 16.0);
            maxX = Math.Max(maxX, e.C2X + 16.0);
        }
        return maxX;
    }

    private static double ComputeRequiredMaxBottom(List<PlannedEdge> planned, double canvasHeight)
    {
        var maxY = canvasHeight;
        foreach (var e in planned)
        {
            maxY = Math.Max(maxY, e.C1Y + 12.0);
            maxY = Math.Max(maxY, e.C2Y + 12.0);
        }
        return maxY;
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
                $"<rect class=\"svg-sg-box\" x=\"{F(sg.X)}\" y=\"{F(sg.Y)}\" width=\"{F(sg.Width)}\" height=\"{F(sg.Height)}\" " +
                $"rx=\"10\" fill=\"#f8fafc\" fill-opacity=\"0.55\" stroke=\"{E(stroke)}\" stroke-width=\"1.5\" stroke-dasharray=\"{E(dash)}\"/>");
            svg.Append(
                $"<line class=\"svg-sg-divider\" x1=\"{F(sg.X + 8)}\" y1=\"{F(sg.Y + 28)}\" x2=\"{F(sg.X + sg.Width - 8)}\" y2=\"{F(sg.Y + 28)}\" " +
                "stroke=\"#e2e8f0\" stroke-width=\"1\"/>");
            svg.Append(
                $"<text class=\"svg-sg-label\" x=\"{F(sg.X + 12)}\" y=\"{F(sg.Y + 20)}\" fill=\"#334155\" " +
                $"font-family=\"system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif\" " +
                $"font-size=\"12\" font-weight=\"600\">{E(sg.Label)}</text>");
            svg.Append("</g>");
        }
    }

    private static void WriteEdgePaths(StringBuilder svg, List<PlannedEdge> planned)
    {
        foreach (var item in planned)
        {
            var stroke = item.LinkStyle?.Stroke ?? "#475569";
            var strokeWidth = item.LinkStyle?.StrokeWidth ?? "1.5";
            var dash = item.LinkStyle?.StrokeDasharray ?? (item.Edge.IsDotted ? "4 3" : null);
            var marker = item.IsViolation ? "url(#arrow-violation)" : "url(#arrow-default)";
            var violationCls = item.IsViolation ? " is-violation" : "";

            svg.Append($"<g class=\"edge{violationCls}\" data-source=\"{E(item.Source.Id)}\" data-target=\"{E(item.Target.Id)}\">");
            if (!string.IsNullOrWhiteSpace(item.Edge.Label))
                svg.Append($"<title>{E(item.Edge.Label)}</title>");
            svg.Append(
                $"<path class=\"svg-edge-path{violationCls}\" d=\"{item.PathData}\" fill=\"none\" " +
                $"stroke=\"{E(stroke)}\" stroke-width=\"{E(strokeWidth)}\"");
            if (!string.IsNullOrEmpty(dash))
                svg.Append($" stroke-dasharray=\"{E(dash)}\"");
            svg.Append($" marker-end=\"{marker}\"/>");
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
            var rx = node.IsStadium ? node.Height / 2.0 : 8.0;
            var roleAttr = string.IsNullOrEmpty(node.ClassName) ? "" : $" data-role=\"{E(node.ClassName)}\"";

            svg.Append($"<g class=\"node\" data-id=\"{E(node.Id)}\">");
            svg.Append(
                $"<rect x=\"{F(node.X)}\" y=\"{F(node.Y)}\" width=\"{F(node.Width)}\" height=\"{F(node.Height)}\" " +
                $"class=\"svg-node-box\"{roleAttr} rx=\"{F(rx)}\" fill=\"{E(fill)}\" stroke=\"{E(stroke)}\" stroke-width=\"{E(strokeWidth)}\"");
            if (!string.IsNullOrEmpty(dash))
                svg.Append($" stroke-dasharray=\"{E(dash)}\"");
            svg.Append("/>");

            svg.Append(
                $"<text class=\"svg-node-text\"{roleAttr} x=\"{F(node.X + node.Width / 2.0)}\" y=\"{F(node.Y + node.Height / 2.0 + 4.5)}\" " +
                $"text-anchor=\"middle\" fill=\"{E(textColor)}\" " +
                $"font-family=\"system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif\" " +
                $"font-size=\"13\" font-weight=\"500\">{E(node.Label)}</text>");
            svg.Append("</g>");
        }
    }

    private static void WriteEdgeLabels(
        StringBuilder svg,
        FlowchartModel model,
        List<PlannedEdge> planned,
        double canvasWidth,
        double canvasHeight)
    {
        var nodeBoxes = model.NodeOrder
            .Select(n => (X: n.X - NodeBoxMargin, Y: n.Y - NodeBoxMargin, W: n.Width + NodeBoxMargin * 2, H: n.Height + NodeBoxMargin * 2))
            .ToList();
        var placedPills = new List<(double X, double Y, double W, double H)>();

        foreach (var item in planned)
        {
            var (lines, pillW, pillH) = FlowchartLayout.MeasureEdgeLabel(item.Edge.Label);
            if (lines.Count == 0)
                continue;

            double bestX = 0, bestY = 0;
            var found = false;

            foreach (var dy in CandidateDys)
            {
                foreach (var t in CandidateTs)
                {
                    EvaluateBezier(item, t, out var bx, out var by);
                    var px = Math.Clamp(bx - pillW / 2.0, CanvasPad, Math.Max(CanvasPad, canvasWidth - CanvasPad - pillW));
                    var py = Math.Clamp(by + dy - pillH / 2.0, CanvasPad, Math.Max(CanvasPad, canvasHeight - CanvasPad - pillH));

                    if (px < CanvasPad || py < CanvasPad ||
                        px + pillW > canvasWidth - CanvasPad ||
                        py + pillH > canvasHeight - CanvasPad)
                    {
                        continue;
                    }

                    if (OverlapsAny(px, py, pillW, pillH, nodeBoxes, margin: 0.0))
                        continue;

                    if (OverlapsAny(px, py, pillW, pillH, placedPills, margin: PillGap))
                        continue;

                    bestX = px;
                    bestY = py;
                    found = true;
                    break;
                }
                if (found)
                    break;
            }

            if (!found)
                continue;

            placedPills.Add((bestX, bestY, pillW, pillH));
            var midX = bestX + pillW / 2.0;
            var midY = bestY + pillH / 2.0;
            var labelStroke = item.IsViolation ? "#dc2626" : "#cbd5e1";
            var labelTextFill = item.IsViolation ? "#991b1b" : "#334155";
            var violationCls = item.IsViolation ? " is-violation" : "";

            svg.Append(
                $"<g class=\"edge-label{violationCls}\" data-source=\"{E(item.Source.Id)}\" data-target=\"{E(item.Target.Id)}\">");
            if (!string.IsNullOrWhiteSpace(item.Edge.Label))
                svg.Append($"<title>{E(item.Edge.Label)}</title>");
            svg.Append(
                $"<rect class=\"svg-edge-pill\" x=\"{F(bestX)}\" y=\"{F(bestY)}\" " +
                $"width=\"{F(pillW)}\" height=\"{F(pillH)}\" rx=\"5\" " +
                $"fill=\"#ffffff\" fill-opacity=\"0.96\" stroke=\"{labelStroke}\" stroke-width=\"1\"/>");

            if (lines.Count == 1)
            {
                svg.Append(
                    $"<text class=\"svg-edge-text\" x=\"{F(midX)}\" y=\"{F(midY + 4.0)}\" text-anchor=\"middle\" " +
                    $"fill=\"{labelTextFill}\" font-family=\"system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif\" " +
                    $"font-size=\"11\">{E(lines[0])}</text>");
            }
            else
            {
                svg.Append(
                    $"<text class=\"svg-edge-text\" x=\"{F(midX)}\" y=\"{F(midY - 3.5)}\" text-anchor=\"middle\" " +
                    $"fill=\"{labelTextFill}\" font-family=\"system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif\" " +
                    $"font-size=\"11\">{E(lines[0])}</text>");
                svg.Append(
                    $"<text class=\"svg-edge-text\" x=\"{F(midX)}\" y=\"{F(midY + 10.5)}\" text-anchor=\"middle\" " +
                    $"fill=\"{labelTextFill}\" font-family=\"system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif\" " +
                    $"font-size=\"11\">{E(lines[1])}</text>");
            }
            svg.Append("</g>");
        }
    }

    private static void EvaluateBezier(PlannedEdge e, double t, out double x, out double y)
    {
        var u = 1.0 - t;
        var uu = u * u;
        var tt = t * t;
        var uuu = uu * u;
        var ttt = tt * t;
        x = uuu * e.X1 + 3.0 * uu * t * e.C1X + 3.0 * u * tt * e.C2X + ttt * e.X2;
        y = uuu * e.Y1 + 3.0 * uu * t * e.C1Y + 3.0 * u * tt * e.C2Y + ttt * e.Y2;
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
            if (RectsOverlap(x - margin, y - margin, w + margin * 2, h + margin * 2, b.X, b.Y, b.W, b.H))
                return true;
        }
        return false;
    }

    private static bool RectsOverlap(
        double x1,
        double y1,
        double w1,
        double h1,
        double x2,
        double y2,
        double w2,
        double h2)
        => x1 < x2 + w2 && x1 + w1 > x2 && y1 < y2 + h2 && y1 + h1 > y2;
}
