using System.Globalization;
using System.Net;
using System.Text;

namespace CqrsLens;

/// <summary>Diagram rendering mode for HTML architecture reports.</summary>
public enum DiagramRenderMode
{
    /// <summary>Emit <c>&lt;pre class="mermaid"&gt;</c> blocks rendered in the browser by Mermaid.js.</summary>
    Mermaid,

    /// <summary>Compile diagrams to inline <c>&lt;svg&gt;</c> ahead of time so the HTML report requires zero JavaScript.</summary>
    Svg,
}

/// <summary>
/// Deterministic, zero-dependency .NET renderer that compiles CqrsLens Mermaid
/// diagrams (<c>flowchart TD</c>, <c>flowchart LR</c>, and <c>erDiagram</c>) to
/// self-contained inline SVG.
/// </summary>
public static class MermaidSvgRenderer
{
    private const double CharWidth = 7.4;
    private const double NodeHeight = 38.0;
    private const double NodeMinWidth = 110.0;
    private const double NodeHorizontalPadding = 28.0;

    public static string Render(string mermaidCode)
    {
        ArgumentNullException.ThrowIfNull(mermaidCode);

        var lines = mermaidCode
            .ReplaceLineEndings("\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        var firstLine = lines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim() ?? "";
        if (firstLine.StartsWith("erDiagram", StringComparison.Ordinal))
            return RenderErDiagram(lines);

        return RenderFlowchart(lines);
    }

    /// <summary>
    /// Decodes CqrsLens Mermaid entity escapes (<c>#38;</c>, <c>#quot;</c>,
    /// <c>#lt;</c>, <c>#gt;</c>, <c>#124;</c>, <c>#35;</c>) in a single pass.
    /// </summary>
    internal static string DecodeMermaidEntities(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length;)
        {
            if (value[i] == '#')
            {
                if (MatchEntity(value, i, "#38;", out var next))
                {
                    sb.Append('&');
                    i = next;
                    continue;
                }
                if (MatchEntity(value, i, "#quot;", out next))
                {
                    sb.Append('"');
                    i = next;
                    continue;
                }
                if (MatchEntity(value, i, "#lt;", out next))
                {
                    sb.Append('<');
                    i = next;
                    continue;
                }
                if (MatchEntity(value, i, "#gt;", out next))
                {
                    sb.Append('>');
                    i = next;
                    continue;
                }
                if (MatchEntity(value, i, "#124;", out next))
                {
                    sb.Append('|');
                    i = next;
                    continue;
                }
                if (MatchEntity(value, i, "#35;", out next))
                {
                    sb.Append('#');
                    i = next;
                    continue;
                }
            }

            sb.Append(value[i]);
            i++;
        }

        return sb.ToString();
    }

    private static bool MatchEntity(string input, int index, string entity, out int nextIndex)
    {
        if (input.AsSpan(index).StartsWith(entity.AsSpan(), StringComparison.Ordinal))
        {
            nextIndex = index + entity.Length;
            return true;
        }

        nextIndex = index;
        return false;
    }

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? "");

    private static string F(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    // =========================================================================
    // Flowchart parser & renderer (flowchart TD / flowchart LR)
    // =========================================================================

    private static string RenderFlowchart(string[] rawLines)
    {
        var isLeftToRight = false;
        string? accTitle = null;
        string? accDescr = null;

        var nodes = new Dictionary<string, FlowNode>(StringComparer.Ordinal);
        var nodeOrder = new List<FlowNode>();
        var subgraphs = new List<FlowSubgraph>();
        var edges = new List<FlowEdge>();
        var classDefs = new Dictionary<string, StyleProps>(StringComparer.Ordinal);
        var nodeStyles = new Dictionary<string, StyleProps>(StringComparer.Ordinal);
        var linkStyles = new Dictionary<int, StyleProps>();

        FlowSubgraph? currentSubgraph = null;

        foreach (var raw in rawLines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("%%", StringComparison.Ordinal))
                continue;

            if (line.StartsWith("flowchart ", StringComparison.Ordinal) ||
                line.StartsWith("graph ", StringComparison.Ordinal))
            {
                var dir = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "TD";
                isLeftToRight = string.Equals(dir, "LR", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(dir, "RL", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (line.StartsWith("accTitle:", StringComparison.Ordinal))
            {
                accTitle = DecodeMermaidEntities(line["accTitle:".Length..].Trim());
                continue;
            }

            if (line.StartsWith("accDescr:", StringComparison.Ordinal))
            {
                accDescr = DecodeMermaidEntities(line["accDescr:".Length..].Trim());
                continue;
            }

            if (line.StartsWith("subgraph ", StringComparison.Ordinal))
            {
                var rest = line["subgraph ".Length..].Trim();
                ParseIdAndLabel(rest, out var sgId, out var sgLabel, out _, out _);
                var sg = new FlowSubgraph(sgId, DecodeMermaidEntities(sgLabel));
                subgraphs.Add(sg);
                currentSubgraph = sg;
                continue;
            }

            if (string.Equals(line, "end", StringComparison.Ordinal))
            {
                currentSubgraph = null;
                continue;
            }

            if (line.StartsWith("classDef ", StringComparison.Ordinal))
            {
                var rest = line["classDef ".Length..].Trim();
                var spaceIdx = rest.IndexOf(' ');
                if (spaceIdx > 0)
                {
                    var className = rest[..spaceIdx].Trim();
                    var props = ParseStyleProps(rest[(spaceIdx + 1)..]);
                    classDefs[className] = props;
                }
                continue;
            }

            if (line.StartsWith("style ", StringComparison.Ordinal))
            {
                var rest = line["style ".Length..].Trim();
                var spaceIdx = rest.IndexOf(' ');
                if (spaceIdx > 0)
                {
                    var targetId = rest[..spaceIdx].Trim();
                    var props = ParseStyleProps(rest[(spaceIdx + 1)..]);
                    nodeStyles[targetId] = props;
                }
                continue;
            }

            if (line.StartsWith("linkStyle ", StringComparison.Ordinal))
            {
                var rest = line["linkStyle ".Length..].Trim();
                var spaceIdx = rest.IndexOf(' ');
                if (spaceIdx > 0 &&
                    int.TryParse(rest[..spaceIdx].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var edgeIdx))
                {
                    linkStyles[edgeIdx] = ParseStyleProps(rest[(spaceIdx + 1)..]);
                }
                continue;
            }

            if (TryParseEdge(line, out var srcSpec, out var dstSpec, out var edgeLabel, out var isDotted))
            {
                var srcNode = EnsureNode(srcSpec, currentSubgraph, nodes, nodeOrder);
                var dstNode = EnsureNode(dstSpec, currentSubgraph, nodes, nodeOrder);
                edges.Add(new FlowEdge(
                    srcNode.Id,
                    dstNode.Id,
                    edgeLabel is null ? null : DecodeMermaidEntities(edgeLabel),
                    isDotted));
                continue;
            }

            EnsureNode(line, currentSubgraph, nodes, nodeOrder);
        }

        // Compute node dimensions from decoded labels.
        foreach (var node in nodeOrder)
        {
            var textWidth = node.Label.Length * CharWidth + NodeHorizontalPadding;
            node.Width = Math.Max(NodeMinWidth, Math.Ceiling(textWidth));
            node.Height = NodeHeight;
        }

        LayoutFlowchart(isLeftToRight, nodeOrder, nodes, subgraphs, edges, out var canvasWidth, out var canvasHeight);

        var svg = new StringBuilder();
        svg.Append("<div class=\"svg-diagram\">");
        svg.Append(
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {F(canvasWidth)} {F(canvasHeight)}\" " +
            $"width=\"{F(canvasWidth)}\" height=\"{F(canvasHeight)}\" role=\"img\">");

        if (!string.IsNullOrEmpty(accTitle))
            svg.Append($"<title>{E(accTitle)}</title>");
        if (!string.IsNullOrEmpty(accDescr))
            svg.Append($"<desc>{E(accDescr)}</desc>");

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

        // Render subgraphs
        foreach (var sg in subgraphs.Where(s => s.Nodes.Count > 0))
        {
            nodeStyles.TryGetValue(sg.Id, out var sgStyle);
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

        // Render edges
        for (var i = 0; i < edges.Count; i++)
        {
            var edge = edges[i];
            if (!nodes.TryGetValue(edge.SourceId, out var src) ||
                !nodes.TryGetValue(edge.TargetId, out var dst))
                continue;

            linkStyles.TryGetValue(i, out var linkStyle);
            var isViolation = linkStyle?.Stroke is "#dc2626";
            var stroke = linkStyle?.Stroke ?? "#475569";
            var strokeWidth = linkStyle?.StrokeWidth ?? "1.5";
            var dash = linkStyle?.StrokeDasharray ?? (edge.IsDotted ? "4 3" : null);
            var marker = isViolation ? "url(#arrow-violation)" : "url(#arrow-default)";

            GetEdgeEndpoints(src, dst, isLeftToRight, out var x1, out var y1, out var x2, out var y2);
            var midX = (x1 + x2) / 2.0;
            var midY = (y1 + y2) / 2.0;

            string pathData;
            if (isLeftToRight)
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

        // Render nodes
        foreach (var node in nodeOrder)
        {
            StyleProps? style = null;
            if (node.ClassName is not null && classDefs.TryGetValue(node.ClassName, out var clsStyle))
                style = clsStyle;
            if (nodeStyles.TryGetValue(node.Id, out var directStyle))
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

        svg.Append("</svg></div>");
        return svg.ToString();
    }

    private static void LayoutFlowchart(
        bool isLeftToRight,
        List<FlowNode> nodeOrder,
        Dictionary<string, FlowNode> nodes,
        List<FlowSubgraph> subgraphs,
        List<FlowEdge> edges,
        out double canvasWidth,
        out double canvasHeight)
    {
        const double outerMargin = 20.0;
        const double hGap = 40.0;
        const double vGap = 44.0;
        const double sgPadX = 18.0;
        const double sgPadTop = 34.0;
        const double sgPadBottom = 16.0;

        if (nodeOrder.Count == 0)
        {
            canvasWidth = 240.0;
            canvasHeight = 80.0;
            return;
        }

        var populatedSubgraphs = subgraphs.Where(s => s.Nodes.Count > 0).ToList();

        if (!isLeftToRight && populatedSubgraphs.Count > 0)
        {
            // Top-down subgraph bands (e.g. LayerGraph)
            var rowWidths = new List<double>();
            foreach (var sg in populatedSubgraphs)
            {
                var nodesWidth = sg.Nodes.Sum(n => n.Width) + Math.Max(0, sg.Nodes.Count - 1) * hGap;
                var labelWidth = sg.Label.Length * CharWidth + 32.0;
                rowWidths.Add(Math.Max(nodesWidth + sgPadX * 2, labelWidth));
            }

            var ungrouped = nodeOrder.Where(n => n.Subgraph is null).ToList();
            if (ungrouped.Count > 0)
            {
                var ungroupedWidth = ungrouped.Sum(n => n.Width) + Math.Max(0, ungrouped.Count - 1) * hGap;
                rowWidths.Add(ungroupedWidth);
            }

            var maxRowWidth = Math.Max(260.0, rowWidths.DefaultIfEmpty(260.0).Max());
            canvasWidth = maxRowWidth + outerMargin * 2;
            var currentY = outerMargin;

            for (var i = 0; i < populatedSubgraphs.Count; i++)
            {
                var sg = populatedSubgraphs[i];
                var sgWidth = rowWidths[i];
                sg.X = (canvasWidth - sgWidth) / 2.0;
                sg.Y = currentY;
                sg.Width = sgWidth;
                sg.Height = sgPadTop + NodeHeight + sgPadBottom;

                var totalNodesW = sg.Nodes.Sum(n => n.Width) + Math.Max(0, sg.Nodes.Count - 1) * hGap;
                var startX = sg.X + (sg.Width - totalNodesW) / 2.0;
                var nodeY = sg.Y + sgPadTop;

                foreach (var node in sg.Nodes)
                {
                    node.X = startX;
                    node.Y = nodeY;
                    startX += node.Width + hGap;
                }

                currentY += sg.Height + vGap;
            }

            if (ungrouped.Count > 0)
            {
                var totalNodesW = ungrouped.Sum(n => n.Width) + Math.Max(0, ungrouped.Count - 1) * hGap;
                var startX = (canvasWidth - totalNodesW) / 2.0;
                foreach (var node in ungrouped)
                {
                    node.X = startX;
                    node.Y = currentY;
                    startX += node.Width + hGap;
                }

                currentY += NodeHeight + vGap;
            }

            canvasHeight = Math.Max(100.0, currentY - vGap + outerMargin);
            return;
        }

        if (isLeftToRight && populatedSubgraphs.Count > 0)
        {
            // Left-to-right CQRS Map layout:
            // Column 0: Validators (or non-subgraph predecessors of subgraph nodes)
            // Column 1: Subgraphs with Request nodes
            // Column 2: Handlers (or non-subgraph targets of subgraph nodes)
            var subgraphNodeIds = populatedSubgraphs
                .SelectMany(s => s.Nodes)
                .Select(n => n.Id)
                .ToHashSet(StringComparer.Ordinal);
            var rightIds = edges
                .Where(e => subgraphNodeIds.Contains(e.SourceId) && !subgraphNodeIds.Contains(e.TargetId))
                .Select(e => e.TargetId)
                .ToHashSet(StringComparer.Ordinal);
            var leftIds = edges
                .Where(e => !subgraphNodeIds.Contains(e.SourceId) && subgraphNodeIds.Contains(e.TargetId))
                .Select(e => e.SourceId)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var n in nodeOrder.Where(n => n.Subgraph is null))
            {
                if (!leftIds.Contains(n.Id) && !rightIds.Contains(n.Id))
                {
                    if (n.IsStadium || string.Equals(n.ClassName, "clsHandler", StringComparison.Ordinal))
                        rightIds.Add(n.Id);
                    else
                        leftIds.Add(n.Id);
                }
            }

            var leftNodes = nodeOrder.Where(n => leftIds.Contains(n.Id)).ToList();
            var rightNodes = nodeOrder.Where(n => rightIds.Contains(n.Id)).ToList();

            const double colGap = 86.0;
            const double innerVGap = 16.0;

            var currentX = outerMargin;
            if (leftNodes.Count > 0)
            {
                var leftWidth = leftNodes.Max(n => n.Width);
                var y = outerMargin + sgPadTop;
                foreach (var node in leftNodes)
                {
                    node.X = currentX + (leftWidth - node.Width) / 2.0;
                    node.Y = y;
                    y += node.Height + innerVGap;
                }
                currentX += leftWidth + colGap;
            }

            var sgColWidth = populatedSubgraphs
                .Select(sg => Math.Max(
                    sg.Nodes.Max(n => n.Width) + sgPadX * 2,
                    sg.Label.Length * CharWidth + 32.0))
                .DefaultIfEmpty(200.0)
                .Max();

            var sgY = outerMargin;
            foreach (var sg in populatedSubgraphs)
            {
                sg.X = currentX;
                sg.Y = sgY;
                sg.Width = sgColWidth;
                var nodesH = sg.Nodes.Count * NodeHeight + Math.Max(0, sg.Nodes.Count - 1) * innerVGap;
                sg.Height = sgPadTop + nodesH + sgPadBottom;

                var ny = sg.Y + sgPadTop;
                foreach (var node in sg.Nodes)
                {
                    node.X = sg.X + (sg.Width - node.Width) / 2.0;
                    node.Y = ny;
                    ny += node.Height + innerVGap;
                }

                sgY += sg.Height + 24.0;
            }

            currentX += sgColWidth;

            if (rightNodes.Count > 0)
            {
                currentX += colGap;
                var rightWidth = rightNodes.Max(n => n.Width);
                var y = outerMargin + sgPadTop;
                foreach (var node in rightNodes)
                {
                    node.X = currentX + (rightWidth - node.Width) / 2.0;
                    node.Y = y;
                    y += node.Height + innerVGap;
                }
                currentX += rightWidth;
            }

            canvasWidth = currentX + outerMargin;
            var maxNodeBottom = nodeOrder.Max(n => n.Y + n.Height);
            var maxSgBottom = populatedSubgraphs.Max(s => s.Y + s.Height);
            canvasHeight = Math.Max(maxNodeBottom, maxSgBottom) + outerMargin;
            return;
        }

        // General rank-based layout (for ProjectGraph and arbitrary flowcharts)
        var ranks = ComputeRanks(nodeOrder, edges);
        var rankGroups = nodeOrder
            .GroupBy(n => ranks[n.Id])
            .OrderBy(g => g.Key)
            .Select(g => g.ToList())
            .ToList();

        if (!isLeftToRight)
        {
            var maxWidth = rankGroups
                .Select(g => g.Sum(n => n.Width) + Math.Max(0, g.Count - 1) * hGap)
                .DefaultIfEmpty(200.0)
                .Max();
            canvasWidth = Math.Max(220.0, maxWidth + outerMargin * 2);
            var y = outerMargin;
            foreach (var group in rankGroups)
            {
                var rowW = group.Sum(n => n.Width) + Math.Max(0, group.Count - 1) * hGap;
                var x = (canvasWidth - rowW) / 2.0;
                foreach (var node in group)
                {
                    node.X = x;
                    node.Y = y;
                    x += node.Width + hGap;
                }
                y += NodeHeight + vGap;
            }

            canvasHeight = Math.Max(80.0, y - vGap + outerMargin);
        }
        else
        {
            var maxHeight = rankGroups
                .Select(g => g.Count * NodeHeight + Math.Max(0, g.Count - 1) * 20.0)
                .DefaultIfEmpty(NodeHeight)
                .Max();
            canvasHeight = Math.Max(80.0, maxHeight + outerMargin * 2);
            var x = outerMargin;
            foreach (var group in rankGroups)
            {
                var colW = group.Max(n => n.Width);
                var colH = group.Count * NodeHeight + Math.Max(0, group.Count - 1) * 20.0;
                var y = (canvasHeight - colH) / 2.0;
                foreach (var node in group)
                {
                    node.X = x + (colW - node.Width) / 2.0;
                    node.Y = y;
                    y += NodeHeight + 20.0;
                }
                x += colW + 72.0;
            }

            canvasWidth = Math.Max(220.0, x - 72.0 + outerMargin);
        }
    }

    private static Dictionary<string, int> ComputeRanks(
        List<FlowNode> nodeOrder,
        List<FlowEdge> edges)
    {
        var ranks = nodeOrder.ToDictionary(n => n.Id, _ => 0, StringComparer.Ordinal);
        var outgoing = nodeOrder.ToDictionary(n => n.Id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            if (outgoing.TryGetValue(edge.SourceId, out var list) &&
                ranks.ContainsKey(edge.TargetId) &&
                !string.Equals(edge.SourceId, edge.TargetId, StringComparison.Ordinal))
            {
                list.Add(edge.TargetId);
            }
        }

        // Bounded relaxation pass (handles DAGs and terminates cleanly on cycles)
        var maxIterations = Math.Max(1, nodeOrder.Count);
        for (var iter = 0; iter < maxIterations; iter++)
        {
            var changed = false;
            foreach (var node in nodeOrder)
            {
                var srcRank = ranks[node.Id];
                foreach (var targetId in outgoing[node.Id])
                {
                    if (ranks[targetId] < srcRank + 1 && srcRank + 1 < nodeOrder.Count)
                    {
                        ranks[targetId] = srcRank + 1;
                        changed = true;
                    }
                }
            }
            if (!changed)
                break;
        }

        return ranks;
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

    private static bool TryParseEdge(
        string line,
        out string srcSpec,
        out string dstSpec,
        out string? label,
        out bool isDotted)
    {
        srcSpec = "";
        dstSpec = "";
        label = null;
        isDotted = false;

        var arrowIdx = line.IndexOf("-.->", StringComparison.Ordinal);
        var arrowLen = 4;
        if (arrowIdx >= 0)
        {
            isDotted = true;
        }
        else
        {
            arrowIdx = line.IndexOf("-->", StringComparison.Ordinal);
            arrowLen = 3;
        }

        if (arrowIdx <= 0)
            return false;

        srcSpec = line[..arrowIdx].Trim();
        var rhs = line[(arrowIdx + arrowLen)..].Trim();
        if (rhs.StartsWith('|'))
        {
            var closePipe = rhs.IndexOf('|', 1);
            if (closePipe > 0)
            {
                label = rhs[1..closePipe];
                rhs = rhs[(closePipe + 1)..].Trim();
            }
        }

        dstSpec = rhs;
        return srcSpec.Length > 0 && dstSpec.Length > 0;
    }

    private static FlowNode EnsureNode(
        string spec,
        FlowSubgraph? currentSubgraph,
        Dictionary<string, FlowNode> nodes,
        List<FlowNode> nodeOrder)
    {
        ParseIdAndLabel(spec, out var id, out var rawLabel, out var isStadium, out var className);
        var decodedLabel = DecodeMermaidEntities(rawLabel);

        if (!nodes.TryGetValue(id, out var node))
        {
            node = new FlowNode(id, decodedLabel)
            {
                IsStadium = isStadium,
                ClassName = className,
                Subgraph = currentSubgraph,
            };
            nodes[id] = node;
            nodeOrder.Add(node);
            currentSubgraph?.Nodes.Add(node);
        }
        else
        {
            if (!string.Equals(rawLabel, id, StringComparison.Ordinal))
                node.Label = decodedLabel;
            if (isStadium)
                node.IsStadium = true;
            if (className is not null)
                node.ClassName = className;
            if (node.Subgraph is null && currentSubgraph is not null)
            {
                node.Subgraph = currentSubgraph;
                currentSubgraph.Nodes.Add(node);
            }
        }

        return node;
    }

    private static void ParseIdAndLabel(
        string raw,
        out string id,
        out string label,
        out bool isStadium,
        out string? className)
    {
        isStadium = false;
        className = null;
        var working = raw.Trim();

        var classSep = working.LastIndexOf(":::", StringComparison.Ordinal);
        if (classSep > 0)
        {
            className = working[(classSep + 3)..].Trim();
            working = working[..classSep].Trim();
        }

        var stadiumStart = working.IndexOf("([\"", StringComparison.Ordinal);
        if (stadiumStart > 0 && working.EndsWith("\"])", StringComparison.Ordinal))
        {
            id = working[..stadiumStart].Trim();
            label = working[(stadiumStart + 3)..^3];
            isStadium = true;
            return;
        }

        var rectStart = working.IndexOf("[\"", StringComparison.Ordinal);
        if (rectStart > 0 && working.EndsWith("\"]", StringComparison.Ordinal))
        {
            id = working[..rectStart].Trim();
            label = working[(rectStart + 2)..^2];
            return;
        }

        id = working;
        label = working;
    }

    private static StyleProps ParseStyleProps(string raw)
    {
        var props = new StyleProps();
        var parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            var colon = part.IndexOf(':');
            if (colon <= 0) continue;
            var key = part[..colon].Trim();
            var val = part[(colon + 1)..].Trim();
            switch (key)
            {
                case "fill":
                    props.Fill = val;
                    break;
                case "stroke":
                    props.Stroke = val;
                    break;
                case "stroke-width":
                    props.StrokeWidth = val.EndsWith("px", StringComparison.OrdinalIgnoreCase)
                        ? val[..^2]
                        : val;
                    break;
                case "stroke-dasharray":
                    props.StrokeDasharray = val;
                    break;
                case "color":
                    props.Color = val;
                    break;
            }
        }

        return props;
    }

    // =========================================================================
    // ER diagram parser & renderer (erDiagram)
    // =========================================================================

    private static string RenderErDiagram(string[] rawLines)
    {
        string? accTitle = null;
        string? accDescr = null;
        var entities = new List<ErEntityBox>();
        var entityById = new Dictionary<string, ErEntityBox>(StringComparer.Ordinal);
        var relationships = new List<ErRelationshipEdge>();

        ErEntityBox? currentEntity = null;

        foreach (var raw in rawLines)
        {
            var line = raw.Trim();
            if (line.Length == 0 ||
                string.Equals(line, "erDiagram", StringComparison.Ordinal) ||
                line.StartsWith("%%", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith("accTitle:", StringComparison.Ordinal))
            {
                accTitle = DecodeMermaidEntities(line["accTitle:".Length..].Trim());
                continue;
            }

            if (line.StartsWith("accDescr:", StringComparison.Ordinal))
            {
                accDescr = DecodeMermaidEntities(line["accDescr:".Length..].Trim());
                continue;
            }

            if (currentEntity is not null)
            {
                if (string.Equals(line, "}", StringComparison.Ordinal))
                {
                    currentEntity = null;
                    continue;
                }

                var tokens = line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (tokens.Length >= 2)
                {
                    var attrType = tokens[0];
                    var attrName = tokens[1];
                    var keys = tokens.Length == 3 ? tokens[2] : "";
                    currentEntity.Attributes.Add(new ErAttributeRow(attrType, attrName, keys));
                }
                continue;
            }

            if (TryParseErRelationship(line, out var rel))
            {
                relationships.Add(rel!);
                continue;
            }

            var opensBlock = line.EndsWith('{');
            var headerPart = opensBlock ? line[..^1].Trim() : line;
            ParseIdAndLabel(headerPart, out var entId, out var entLabel, out _, out _);
            var decodedLabel = DecodeMermaidEntities(entLabel);
            if (!entityById.TryGetValue(entId, out var entity))
            {
                entity = new ErEntityBox(entId, decodedLabel);
                entityById[entId] = entity;
                entities.Add(entity);
            }

            if (opensBlock)
                currentEntity = entity;
        }

        const double headerHeight = 32.0;
        const double rowHeight = 24.0;
        const double outerMargin = 24.0;
        const double colGap = 90.0;
        const double rowGap = 64.0;

        foreach (var entity in entities)
        {
            var headerW = entity.Label.Length * 7.8 + 28.0;
            var maxAttrW = 0.0;
            foreach (var attr in entity.Attributes)
            {
                var w = (attr.Type.Length + attr.Name.Length + attr.Keys.Length) * 7.0 + 48.0;
                if (w > maxAttrW) maxAttrW = w;
            }

            entity.Width = Math.Max(160.0, Math.Ceiling(Math.Max(headerW, maxAttrW)));
            entity.Height = headerHeight + Math.Max(1, entity.Attributes.Count) * rowHeight;
            if (entity.Attributes.Count == 0)
                entity.Height = headerHeight;
        }

        var columns = Math.Max(1, Math.Min(3, entities.Count));
        var colWidths = new double[columns];
        var rowCount = (entities.Count + columns - 1) / columns;
        var rowHeights = new double[ Math.Max(1, rowCount) ];

        for (var i = 0; i < entities.Count; i++)
        {
            var col = i % columns;
            var row = i / columns;
            if (entities[i].Width > colWidths[col])
                colWidths[col] = entities[i].Width;
            if (entities[i].Height > rowHeights[row])
                rowHeights[row] = entities[i].Height;
        }

        for (var i = 0; i < entities.Count; i++)
        {
            var col = i % columns;
            var row = i / columns;
            var x = outerMargin;
            for (var c = 0; c < col; c++)
                x += colWidths[c] + colGap;
            var y = outerMargin;
            for (var r = 0; r < row; r++)
                y += rowHeights[r] + rowGap;

            entities[i].X = x + (colWidths[col] - entities[i].Width) / 2.0;
            entities[i].Y = y;
        }

        var canvasWidth = Math.Max(
            240.0,
            outerMargin * 2 + colWidths.Sum() + Math.Max(0, columns - 1) * colGap);
        var canvasHeight = Math.Max(
            100.0,
            outerMargin * 2 + rowHeights.Sum() + Math.Max(0, rowCount - 1) * rowGap);

        var svg = new StringBuilder();
        svg.Append("<div class=\"svg-diagram\">");
        svg.Append(
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {F(canvasWidth)} {F(canvasHeight)}\" " +
            $"width=\"{F(canvasWidth)}\" height=\"{F(canvasHeight)}\" role=\"img\">");

        if (!string.IsNullOrEmpty(accTitle))
            svg.Append($"<title>{E(accTitle)}</title>");
        if (!string.IsNullOrEmpty(accDescr))
            svg.Append($"<desc>{E(accDescr)}</desc>");

        // Render relationship lines and labels
        foreach (var rel in relationships)
        {
            if (!entityById.TryGetValue(rel.PrincipalId, out var principal) ||
                !entityById.TryGetValue(rel.DependentId, out var dependent))
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

        // Render entity boxes
        foreach (var entity in entities)
        {
            svg.Append($"<g class=\"er-entity\" data-id=\"{E(entity.Id)}\">");
            svg.Append(
                $"<rect x=\"{F(entity.X)}\" y=\"{F(entity.Y)}\" width=\"{F(entity.Width)}\" height=\"{F(entity.Height)}\" " +
                "rx=\"6\" fill=\"#ffffff\" stroke=\"#2563eb\" stroke-width=\"1.5\"/>");
            svg.Append(
                $"<rect x=\"{F(entity.X)}\" y=\"{F(entity.Y)}\" width=\"{F(entity.Width)}\" height=\"{F(headerHeight)}\" " +
                "rx=\"6\" fill=\"#eff6ff\" stroke=\"#2563eb\" stroke-width=\"1.5\"/>");
            svg.Append(
                $"<text x=\"{F(entity.X + entity.Width / 2.0)}\" y=\"{F(entity.Y + 21)}\" " +
                "text-anchor=\"middle\" fill=\"#1e3a8a\" " +
                "font-family=\"system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif\" " +
                $"font-size=\"13\" font-weight=\"600\">{E(entity.Label)}</text>");

            for (var r = 0; r < entity.Attributes.Count; r++)
            {
                var attr = entity.Attributes[r];
                var rowY = entity.Y + headerHeight + r * rowHeight;
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

        svg.Append("</svg></div>");
        return svg.ToString();
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

    private static bool TryParseErRelationship(string line, out ErRelationshipEdge? edge)
    {
        edge = null;
        string[] connectors = { "||--o{", "|o--o{", "||--o|", "|o--o|", "}o--o{" };
        foreach (var connector in connectors)
        {
            var idx = line.IndexOf(connector, StringComparison.Ordinal);
            if (idx <= 0) continue;

            var principalId = line[..idx].Trim();
            var rest = line[(idx + connector.Length)..].Trim();
            var colonIdx = rest.IndexOf(':');
            if (colonIdx <= 0) continue;

            var dependentId = rest[..colonIdx].Trim();
            var rawLabel = rest[(colonIdx + 1)..].Trim();
            if (rawLabel.Length >= 2 && rawLabel.StartsWith('"') && rawLabel.EndsWith('"'))
                rawLabel = rawLabel[1..^1];

            edge = new ErRelationshipEdge(
                principalId,
                dependentId,
                connector,
                DecodeMermaidEntities(rawLabel));
            return true;
        }

        return false;
    }

    private sealed class FlowNode
    {
        public FlowNode(string id, string label)
        {
            Id = id;
            Label = label;
        }

        public string Id { get; }
        public string Label { get; set; }
        public bool IsStadium { get; set; }
        public string? ClassName { get; set; }
        public FlowSubgraph? Subgraph { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }

    private sealed class FlowSubgraph
    {
        public FlowSubgraph(string id, string label)
        {
            Id = id;
            Label = label;
        }

        public string Id { get; }
        public string Label { get; }
        public List<FlowNode> Nodes { get; } = new();
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }

    private sealed record FlowEdge(
        string SourceId,
        string TargetId,
        string? Label,
        bool IsDotted);

    private sealed class StyleProps
    {
        public string? Fill { get; set; }
        public string? Stroke { get; set; }
        public string? StrokeWidth { get; set; }
        public string? StrokeDasharray { get; set; }
        public string? Color { get; set; }
    }

    private sealed class ErEntityBox
    {
        public ErEntityBox(string id, string label)
        {
            Id = id;
            Label = label;
        }

        public string Id { get; }
        public string Label { get; }
        public List<ErAttributeRow> Attributes { get; } = new();
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }

    private sealed record ErAttributeRow(string Type, string Name, string Keys);

    private sealed record ErRelationshipEdge(
        string PrincipalId,
        string DependentId,
        string Connector,
        string Label);
}
