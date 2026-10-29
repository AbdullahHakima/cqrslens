namespace CqrsLens;

/// <summary>Assigns deterministic coordinates to flowchart nodes and subgraphs.</summary>
internal static class FlowchartLayout
{
    private const double CharWidth = 7.4;
    private const double NodeHeight = 38.0;
    private const double NodeMinWidth = 110.0;
    private const double NodeHorizontalPadding = 28.0;

    internal static (double Width, double Height) Apply(FlowchartModel model)
    {
        foreach (var node in model.NodeOrder)
        {
            var textWidth = node.Label.Length * CharWidth + NodeHorizontalPadding;
            node.Width = Math.Max(NodeMinWidth, Math.Ceiling(textWidth));
            node.Height = NodeHeight;
        }

        return Layout(
            model.IsLeftToRight,
            model.NodeOrder,
            model.Subgraphs,
            model.Edges);
    }

    private static (double Width, double Height) Layout(
        bool isLeftToRight,
        List<FlowNode> nodeOrder,
        List<FlowSubgraph> subgraphs,
        List<FlowEdge> edges)
    {
        const double outerMargin = 20.0;
        const double hGap = 40.0;
        const double vGap = 44.0;
        const double sgPadX = 18.0;
        const double sgPadTop = 34.0;
        const double sgPadBottom = 16.0;

        if (nodeOrder.Count == 0)
            return (240.0, 80.0);

        var populatedSubgraphs = subgraphs.Where(s => s.Nodes.Count > 0).ToList();

        if (!isLeftToRight && populatedSubgraphs.Count > 0)
            return LayoutTopDownBands(nodeOrder, populatedSubgraphs, outerMargin, hGap, vGap, sgPadX, sgPadTop, sgPadBottom);

        if (isLeftToRight && populatedSubgraphs.Count > 0)
            return LayoutLeftToRightMap(nodeOrder, populatedSubgraphs, edges, outerMargin, sgPadX, sgPadTop, sgPadBottom);

        return LayoutByRank(isLeftToRight, nodeOrder, edges, outerMargin, hGap, vGap);
    }

    private static (double Width, double Height) LayoutTopDownBands(
        List<FlowNode> nodeOrder,
        List<FlowSubgraph> populatedSubgraphs,
        double outerMargin,
        double hGap,
        double vGap,
        double sgPadX,
        double sgPadTop,
        double sgPadBottom)
    {
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
        var canvasWidth = maxRowWidth + outerMargin * 2;
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

        var canvasHeight = Math.Max(100.0, currentY - vGap + outerMargin);
        return (canvasWidth, canvasHeight);
    }

    private static (double Width, double Height) LayoutLeftToRightMap(
        List<FlowNode> nodeOrder,
        List<FlowSubgraph> populatedSubgraphs,
        List<FlowEdge> edges,
        double outerMargin,
        double sgPadX,
        double sgPadTop,
        double sgPadBottom)
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

        var canvasWidth = currentX + outerMargin;
        var maxNodeBottom = nodeOrder.Max(n => n.Y + n.Height);
        var maxSgBottom = populatedSubgraphs.Max(s => s.Y + s.Height);
        var canvasHeight = Math.Max(maxNodeBottom, maxSgBottom) + outerMargin;
        return (canvasWidth, canvasHeight);
    }

    private static (double Width, double Height) LayoutByRank(
        bool isLeftToRight,
        List<FlowNode> nodeOrder,
        List<FlowEdge> edges,
        double outerMargin,
        double hGap,
        double vGap)
    {
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
            var canvasWidth = Math.Max(220.0, maxWidth + outerMargin * 2);
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

            var canvasHeight = Math.Max(80.0, y - vGap + outerMargin);
            return (canvasWidth, canvasHeight);
        }

        {
            var maxHeight = rankGroups
                .Select(g => g.Count * NodeHeight + Math.Max(0, g.Count - 1) * 20.0)
                .DefaultIfEmpty(NodeHeight)
                .Max();
            var canvasHeight = Math.Max(80.0, maxHeight + outerMargin * 2);
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

            var canvasWidth = Math.Max(220.0, x - 72.0 + outerMargin);
            return (canvasWidth, canvasHeight);
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
}
