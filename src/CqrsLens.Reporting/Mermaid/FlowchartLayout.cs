namespace CqrsLens;

/// <summary>Assigns deterministic, overlap-free coordinates to flowchart nodes and subgraphs.</summary>
internal static class FlowchartLayout
{
    internal const double CharWidth = 7.8;
    internal const double NodeHeight = 40.0;
    private const double NodeMinWidth = 116.0;
    private const double NodeHorizontalPadding = 32.0;
    private const double MaxRowWidth = 1200.0;
    private const double InnerRowGap = 24.0;
    private const double OuterMargin = 24.0;
    private const double SubColumnGap = 24.0;
    private const double InnerVGap = 16.0;
    private const double SgPadX = 20.0;
    private const double SgPadTop = 38.0;
    private const double SgPadBottom = 18.0;

    internal static (IReadOnlyList<string> Lines, double Width, double Height) MeasureEdgeLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return (Array.Empty<string>(), 0.0, 0.0);

        var normalized = label.Trim();
        IReadOnlyList<string> lines;
        if (normalized.Length <= 30)
        {
            lines = new[] { normalized };
        }
        else
        {
            var splitAt = FindWrapSplit(normalized, 34);
            string line1;
            string rest;
            if (splitAt > 0)
            {
                line1 = normalized[..splitAt].TrimEnd();
                rest = normalized[splitAt..].TrimStart();
            }
            else
            {
                line1 = normalized[..32].TrimEnd();
                rest = normalized[32..].TrimStart();
            }

            if (string.IsNullOrEmpty(rest))
            {
                lines = new[] { TruncateLine(line1, 36) };
            }
            else
            {
                lines = new[] { TruncateLine(line1, 34), TruncateLine(rest, 36) };
            }
        }

        var maxLen = lines.Max(l => l.Length);
        var width = Math.Max(48.0, Math.Ceiling(maxLen * 6.8 + 16.0));
        var height = lines.Count == 1 ? 20.0 : 34.0;
        return (lines, width, height);
    }

    private static int FindWrapSplit(string text, int maxFirstLine)
    {
        var ideal = Math.Min(maxFirstLine, text.Length / 2 + 4);
        var best = -1;
        var bestDist = int.MaxValue;
        for (var i = 10; i <= Math.Min(maxFirstLine, text.Length - 4); i++)
        {
            var ch = text[i];
            if (ch is ' ' or ',' or '/' or '-')
            {
                var split = ch == ' ' ? i : i + 1;
                var dist = Math.Abs(split - ideal);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = split;
                }
            }
            else if (ch == '→' && i > 10)
            {
                var dist = Math.Abs(i - ideal);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = i;
                }
            }
        }

        return best;
    }

    private static string TruncateLine(string text, int maxChars)
        => text.Length <= maxChars ? text : text[..(maxChars - 1)].TrimEnd() + "…";

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
        List<FlowchartEdge> edges)
    {
        const double hGap = 40.0;
        const double vGap = 44.0;

        if (nodeOrder.Count == 0)
            return (240.0, 80.0);

        var populatedSubgraphs = subgraphs.Where(s => s.Nodes.Count > 0).ToList();

        if (!isLeftToRight && populatedSubgraphs.Count > 0)
            return LayoutTopDownBands(nodeOrder, populatedSubgraphs, edges, hGap, 56.0);

        if (isLeftToRight && populatedSubgraphs.Count >= 2 && nodeOrder.All(n => n.Subgraph is not null))
            return LayoutLeftToRightSubgraphs(nodeOrder, populatedSubgraphs, edges);

        if (isLeftToRight && populatedSubgraphs.Count > 0 && nodeOrder.Any(n => n.Subgraph is null))
            return LayoutLeftToRightMap(nodeOrder, populatedSubgraphs, edges);

        return LayoutByRank(isLeftToRight, nodeOrder, populatedSubgraphs, edges, hGap, vGap);
    }

    private static List<List<FlowNode>> WrapIntoRows(
        IReadOnlyList<FlowNode> nodes,
        double hGap,
        double maxRowWidth = MaxRowWidth)
    {
        var rows = new List<List<FlowNode>>();
        if (nodes.Count == 0)
            return rows;

        var currentRow = new List<FlowNode>();
        var currentWidth = 0.0;
        foreach (var node in nodes)
        {
            var addedWidth = currentRow.Count == 0 ? node.Width : hGap + node.Width;
            if (currentRow.Count > 0 && currentWidth + addedWidth > maxRowWidth)
            {
                rows.Add(currentRow);
                currentRow = new List<FlowNode> { node };
                currentWidth = node.Width;
            }
            else
            {
                currentRow.Add(node);
                currentWidth += addedWidth;
            }
        }

        if (currentRow.Count > 0)
            rows.Add(currentRow);

        return rows;
    }

    private static (double Width, double Height) LayoutTopDownBands(
        List<FlowNode> nodeOrder,
        List<FlowSubgraph> populatedSubgraphs,
        List<FlowchartEdge> edges,
        double hGap,
        double vGap)
    {
        var ungrouped = nodeOrder.Where(n => n.Subgraph is null).ToList();
        var bands = populatedSubgraphs.Select(s => s.Nodes.ToList()).ToList();
        if (ungrouped.Count > 0)
            bands.Add(ungrouped);
        OrderByBarycenter(bands, edges);

        var sgWrappedRows = new List<List<List<FlowNode>>>(populatedSubgraphs.Count);
        var rowWidths = new List<double>();
        for (var i = 0; i < populatedSubgraphs.Count; i++)
        {
            var sg = populatedSubgraphs[i];
            sg.Nodes.Clear();
            sg.Nodes.AddRange(bands[i]);
            var wrapped = WrapIntoRows(sg.Nodes, hGap);
            sgWrappedRows.Add(wrapped);
            var nodesWidth = wrapped
                .Select(r => r.Sum(n => n.Width) + Math.Max(0, r.Count - 1) * hGap)
                .DefaultIfEmpty(0.0)
                .Max();
            var labelWidth = sg.Label.Length * CharWidth + 36.0;
            rowWidths.Add(Math.Max(nodesWidth + SgPadX * 2, labelWidth));
        }

        var ungroupedRows = ungrouped.Count > 0 ? WrapIntoRows(bands[^1], hGap) : new List<List<FlowNode>>();
        if (ungroupedRows.Count > 0)
        {
            var ungroupedWidth = ungroupedRows
                .Select(r => r.Sum(n => n.Width) + Math.Max(0, r.Count - 1) * hGap)
                .DefaultIfEmpty(0.0)
                .Max();
            rowWidths.Add(ungroupedWidth);
        }

        var maxRowWidth = Math.Max(260.0, rowWidths.DefaultIfEmpty(260.0).Max());
        var canvasWidth = maxRowWidth + OuterMargin * 2;
        var currentY = OuterMargin;

        for (var i = 0; i < populatedSubgraphs.Count; i++)
        {
            var sg = populatedSubgraphs[i];
            var rows = sgWrappedRows[i];
            var rowGap = InnerRowGap;
            if (rows.Count > 1)
            {
                var rowOf = new Dictionary<string, int>(StringComparer.Ordinal);
                for (var rIdx = 0; rIdx < rows.Count; rIdx++)
                {
                    foreach (var n in rows[rIdx])
                        rowOf[n.Id] = rIdx;
                }
                if (edges.Any(e => rowOf.TryGetValue(e.SourceId, out var r1) && rowOf.TryGetValue(e.TargetId, out var r2) && r1 != r2))
                    rowGap = 44.0;
            }
            var sgWidth = rowWidths[i];
            sg.X = (canvasWidth - sgWidth) / 2.0;
            sg.Y = currentY;
            sg.Width = sgWidth;
            var rowsHeight = rows.Count * NodeHeight + Math.Max(0, rows.Count - 1) * rowGap;
            sg.Height = SgPadTop + rowsHeight + SgPadBottom;

            var nodeY = sg.Y + SgPadTop;
            foreach (var row in rows)
            {
                var totalNodesW = row.Sum(n => n.Width) + Math.Max(0, row.Count - 1) * hGap;
                var startX = sg.X + (sg.Width - totalNodesW) / 2.0;
                foreach (var node in row)
                {
                    node.X = startX;
                    node.Y = nodeY;
                    startX += node.Width + hGap;
                }
                nodeY += NodeHeight + rowGap;
            }

            currentY += sg.Height + vGap;
        }

        if (ungroupedRows.Count > 0)
        {
            for (var rIdx = 0; rIdx < ungroupedRows.Count; rIdx++)
            {
                var row = ungroupedRows[rIdx];
                var totalNodesW = row.Sum(n => n.Width) + Math.Max(0, row.Count - 1) * hGap;
                var startX = (canvasWidth - totalNodesW) / 2.0;
                foreach (var node in row)
                {
                    node.X = startX;
                    node.Y = currentY;
                    startX += node.Width + hGap;
                }

                currentY += NodeHeight + (rIdx < ungroupedRows.Count - 1 ? InnerRowGap : vGap);
            }
        }

        var canvasHeight = Math.Max(100.0, currentY - vGap + OuterMargin);
        return (canvasWidth, canvasHeight);
    }

    private static (double Width, double Height) LayoutLeftToRightSubgraphs(
        List<FlowNode> nodeOrder,
        List<FlowSubgraph> populatedSubgraphs,
        List<FlowchartEdge> edges)
    {
        var columns = populatedSubgraphs.Select(s => s.Nodes.ToList()).ToList();
        OrderByBarycenter(columns, edges);

        var colSubCols = new List<List<List<FlowNode>>>(populatedSubgraphs.Count);
        var sgWidths = new double[populatedSubgraphs.Count];
        var sgHeights = new double[populatedSubgraphs.Count];
        var maxSgHeight = 0.0;

        for (var i = 0; i < populatedSubgraphs.Count; i++)
        {
            var sg = populatedSubgraphs[i];
            sg.Nodes.Clear();
            sg.Nodes.AddRange(columns[i]);

            var subCols = SplitColumnIfTall(columns[i], threshold: 10);
            colSubCols.Add(subCols);

            var innerW = subCols.Sum(sc => sc.Max(n => n.Width)) + Math.Max(0, subCols.Count - 1) * SubColumnGap;
            var maxRows = subCols.Max(sc => sc.Count);
            var innerH = maxRows * NodeHeight + Math.Max(0, maxRows - 1) * InnerVGap;

            sgWidths[i] = Math.Max(innerW + SgPadX * 2, sg.Label.Length * CharWidth + 36.0);
            sgHeights[i] = SgPadTop + innerH + SgPadBottom;
            if (sgHeights[i] > maxSgHeight)
                maxSgHeight = sgHeights[i];
        }

        var colGapAfter = ComputeCrossColumnGaps(columns, edges, minGap: 96.0, maxGap: 230.0);
        var currentX = OuterMargin;

        for (var i = 0; i < populatedSubgraphs.Count; i++)
        {
            var sg = populatedSubgraphs[i];
            var subCols = colSubCols[i];
            sg.X = currentX;
            sg.Y = OuterMargin + (maxSgHeight - sgHeights[i]) / 2.0;
            sg.Width = sgWidths[i];
            sg.Height = sgHeights[i];

            var innerW = subCols.Sum(sc => sc.Max(n => n.Width)) + Math.Max(0, subCols.Count - 1) * SubColumnGap;
            var subX = sg.X + (sg.Width - innerW) / 2.0;
            var maxRows = subCols.Max(sc => sc.Count);
            var innerHeight = maxRows * NodeHeight + Math.Max(0, maxRows - 1) * InnerVGap;

            foreach (var subCol in subCols)
            {
                var scW = subCol.Max(n => n.Width);
                var scH = subCol.Count * NodeHeight + Math.Max(0, subCol.Count - 1) * InnerVGap;
                var y = sg.Y + SgPadTop + (innerHeight - scH) / 2.0;
                foreach (var node in subCol)
                {
                    node.X = subX + (scW - node.Width) / 2.0;
                    node.Y = y;
                    y += NodeHeight + InnerVGap;
                }
                subX += scW + SubColumnGap;
            }

            currentX += sg.Width + (i < populatedSubgraphs.Count - 1 ? colGapAfter[i] : 0.0);
        }

        var canvasWidth = Math.Max(260.0, currentX + OuterMargin);
        var canvasHeight = Math.Max(120.0, maxSgHeight + OuterMargin * 2);
        return (canvasWidth, canvasHeight);
    }

    private static (double Width, double Height) LayoutLeftToRightMap(
        List<FlowNode> nodeOrder,
        List<FlowSubgraph> populatedSubgraphs,
        List<FlowchartEdge> edges)
    {
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
        var middleNodes = populatedSubgraphs.SelectMany(s => s.Nodes).ToList();
        var rightNodes = nodeOrder.Where(n => rightIds.Contains(n.Id)).ToList();

        var columns = new List<List<FlowNode>>();
        if (leftNodes.Count > 0) columns.Add(leftNodes);
        columns.Add(middleNodes);
        if (rightNodes.Count > 0) columns.Add(rightNodes);
        OrderByBarycenter(columns, edges);

        var colGapAfter = ComputeCrossColumnGaps(columns, edges, minGap: 96.0, maxGap: 230.0);
        var colIdx = 0;

        var middleOrder = columns[leftNodes.Count > 0 ? 1 : 0]
            .Select((n, idx) => (n.Id, idx))
            .ToDictionary(x => x.Id, x => x.idx, StringComparer.Ordinal);
        foreach (var sg in populatedSubgraphs)
        {
            var sorted = sg.Nodes.OrderBy(n => middleOrder.GetValueOrDefault(n.Id, 0)).ToList();
            sg.Nodes.Clear();
            sg.Nodes.AddRange(sorted);
        }

        var sgColWidth = populatedSubgraphs
            .Select(sg => Math.Max(
                sg.Nodes.Max(n => n.Width) + SgPadX * 2,
                sg.Label.Length * CharWidth + 36.0))
            .DefaultIfEmpty(200.0)
            .Max();

        var middleHeight = populatedSubgraphs.Sum(sg =>
            SgPadTop + sg.Nodes.Count * NodeHeight + Math.Max(0, sg.Nodes.Count - 1) * InnerVGap + SgPadBottom)
            + Math.Max(0, populatedSubgraphs.Count - 1) * 24.0;

        var currentX = OuterMargin;
        if (leftNodes.Count > 0)
        {
            var leftWidth = leftNodes.Max(n => n.Width);
            var leftH = leftNodes.Count * NodeHeight + Math.Max(0, leftNodes.Count - 1) * InnerVGap;
            var y = OuterMargin + Math.Max(SgPadTop, (middleHeight - leftH) / 2.0);
            foreach (var node in leftNodes)
            {
                node.X = currentX + (leftWidth - node.Width) / 2.0;
                node.Y = y;
                y += NodeHeight + InnerVGap;
            }
            currentX += leftWidth + colGapAfter[colIdx++];
        }

        var sgY = OuterMargin;
        foreach (var sg in populatedSubgraphs)
        {
            sg.X = currentX;
            sg.Y = sgY;
            sg.Width = sgColWidth;
            var nodesH = sg.Nodes.Count * NodeHeight + Math.Max(0, sg.Nodes.Count - 1) * InnerVGap;
            sg.Height = SgPadTop + nodesH + SgPadBottom;

            var ny = sg.Y + SgPadTop;
            foreach (var node in sg.Nodes)
            {
                node.X = sg.X + (sg.Width - node.Width) / 2.0;
                node.Y = ny;
                ny += NodeHeight + InnerVGap;
            }

            sgY += sg.Height + 24.0;
        }

        currentX += sgColWidth;

        if (rightNodes.Count > 0)
        {
            currentX += colGapAfter[colIdx];
            var rightWidth = rightNodes.Max(n => n.Width);
            var rightH = rightNodes.Count * NodeHeight + Math.Max(0, rightNodes.Count - 1) * InnerVGap;
            var y = OuterMargin + Math.Max(SgPadTop, (middleHeight - rightH) / 2.0);
            foreach (var node in rightNodes)
            {
                node.X = currentX + (rightWidth - node.Width) / 2.0;
                node.Y = y;
                y += NodeHeight + InnerVGap;
            }
            currentX += rightWidth;
        }

        var canvasWidth = currentX + OuterMargin;
        var maxNodeBottom = nodeOrder.Max(n => n.Y + n.Height);
        var maxSgBottom = populatedSubgraphs.Max(s => s.Y + s.Height);
        var canvasHeight = Math.Max(maxNodeBottom, maxSgBottom) + OuterMargin;
        return (canvasWidth, canvasHeight);
    }

    private static (double Width, double Height) LayoutByRank(
        bool isLeftToRight,
        List<FlowNode> nodeOrder,
        List<FlowSubgraph> populatedSubgraphs,
        List<FlowchartEdge> edges,
        double hGap,
        double vGap)
    {
        var ranks = ComputeRanks(nodeOrder, edges);
        var rankGroups = nodeOrder
            .GroupBy(n => ranks[n.Id])
            .OrderBy(g => g.Key)
            .Select(g => g.ToList())
            .ToList();

        OrderByBarycenter(rankGroups, edges);

        if (!isLeftToRight)
        {
            var wrappedRanks = rankGroups.Select(g => WrapIntoRows(g, hGap)).ToList();
            var maxWidth = wrappedRanks
                .SelectMany(rows => rows)
                .Select(r => r.Sum(n => n.Width) + Math.Max(0, r.Count - 1) * hGap)
                .DefaultIfEmpty(200.0)
                .Max();
            var canvasWidth = Math.Max(220.0, maxWidth + OuterMargin * 2);
            var y = OuterMargin;
            foreach (var rows in wrappedRanks)
            {
                for (var rIdx = 0; rIdx < rows.Count; rIdx++)
                {
                    var row = rows[rIdx];
                    var rowW = row.Sum(n => n.Width) + Math.Max(0, row.Count - 1) * hGap;
                    var x = (canvasWidth - rowW) / 2.0;
                    foreach (var node in row)
                    {
                        node.X = x;
                        node.Y = y;
                        x += node.Width + hGap;
                    }
                    y += NodeHeight + (rIdx < rows.Count - 1 ? InnerRowGap : vGap);
                }
            }

            var canvasHeight = Math.Max(80.0, y - vGap + OuterMargin);
            return (canvasWidth, canvasHeight);
        }

        var hasSubgraphs = populatedSubgraphs.Count > 0;
        var padX = hasSubgraphs ? SgPadX : 0.0;
        var padTop = hasSubgraphs ? SgPadTop : 0.0;
        var padBottom = hasSubgraphs ? SgPadBottom : 0.0;

        var splitRanks = rankGroups.Select(g => SplitColumnIfTall(g, threshold: 14)).ToList();
        var maxHeight = splitRanks
            .Select(subCols =>
            {
                var maxRows = subCols.Max(sc => sc.Count);
                return maxRows * NodeHeight + Math.Max(0, maxRows - 1) * 18.0;
            })
            .DefaultIfEmpty(NodeHeight)
            .Max();

        var colGapAfter = ComputeCrossColumnGaps(rankGroups, edges, minGap: 96.0, maxGap: 260.0);
        var canvasHeightLr = Math.Max(100.0, maxHeight + padTop + padBottom + OuterMargin * 2);
        var xLr = OuterMargin + padX;

        for (var r = 0; r < splitRanks.Count; r++)
        {
            var subCols = splitRanks[r];
            var totalRankWidth = subCols.Sum(sc => sc.Max(n => n.Width)) + Math.Max(0, subCols.Count - 1) * SubColumnGap;
            var subX = xLr;

            foreach (var subCol in subCols)
            {
                var scW = subCol.Max(n => n.Width);
                var colH = subCol.Count * NodeHeight + Math.Max(0, subCol.Count - 1) * 18.0;
                var y = hasSubgraphs
                    ? OuterMargin + padTop + (maxHeight - colH) / 2.0
                    : (canvasHeightLr - colH) / 2.0;
                foreach (var node in subCol)
                {
                    node.X = subX + (scW - node.Width) / 2.0;
                    node.Y = y;
                    y += NodeHeight + 18.0;
                }
                subX += scW + SubColumnGap;
            }

            xLr += totalRankWidth + (r < splitRanks.Count - 1 ? colGapAfter[r] : 0.0);
        }

        if (hasSubgraphs)
        {
            foreach (var sg in populatedSubgraphs)
            {
                var minX = sg.Nodes.Min(n => n.X);
                var maxX = sg.Nodes.Max(n => n.X + n.Width);
                var minY = sg.Nodes.Min(n => n.Y);
                var maxY = sg.Nodes.Max(n => n.Y + n.Height);
                var labelW = sg.Label.Length * CharWidth + 36.0;
                sg.X = minX - SgPadX;
                sg.Y = minY - SgPadTop;
                sg.Width = Math.Max(maxX - minX + SgPadX * 2, labelW);
                sg.Height = (maxY - minY) + SgPadTop + SgPadBottom;
            }

            var minSgX = populatedSubgraphs.Min(s => s.X);
            if (minSgX < OuterMargin)
            {
                var shift = OuterMargin - minSgX;
                foreach (var n in nodeOrder) n.X += shift;
                foreach (var s in populatedSubgraphs) s.X += shift;
            }

            var rightEdge = Math.Max(
                nodeOrder.Max(n => n.X + n.Width),
                populatedSubgraphs.Max(s => s.X + s.Width));
            var bottomEdge = Math.Max(
                nodeOrder.Max(n => n.Y + n.Height),
                populatedSubgraphs.Max(s => s.Y + s.Height));
            return (Math.Max(240.0, rightEdge + OuterMargin), Math.Max(100.0, bottomEdge + OuterMargin));
        }

        var canvasWidthLr = Math.Max(240.0, xLr + OuterMargin);
        return (canvasWidthLr, canvasHeightLr);
    }

    private static List<List<FlowNode>> SplitColumnIfTall(List<FlowNode> column, int threshold)
    {
        if (column.Count <= threshold)
            return new List<List<FlowNode>> { column };

        var mid = (column.Count + 1) / 2;
        return new List<List<FlowNode>>
        {
            column.Take(mid).ToList(),
            column.Skip(mid).ToList(),
        };
    }

    private static double[] ComputeCrossColumnGaps(
        IReadOnlyList<List<FlowNode>> columns,
        IReadOnlyList<FlowchartEdge> edges,
        double minGap,
        double maxGap)
    {
        var count = Math.Max(0, columns.Count - 1);
        var gaps = new double[count];
        if (count == 0)
            return gaps;

        var colOf = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var c = 0; c < columns.Count; c++)
        {
            foreach (var node in columns[c])
                colOf[node.Id] = c;
        }

        for (var c = 0; c < count; c++)
            gaps[c] = minGap;

        foreach (var edge in edges)
        {
            if (!colOf.TryGetValue(edge.SourceId, out var c1) ||
                !colOf.TryGetValue(edge.TargetId, out var c2) ||
                c1 == c2)
            {
                continue;
            }

            var (_, pillW, _) = MeasureEdgeLabel(edge.Label);
            if (pillW <= 0)
                continue;

            var lo = Math.Min(c1, c2);
            var hi = Math.Max(c1, c2);
            var needed = Math.Clamp(pillW + 52.0, minGap, maxGap);
            if (hi - lo == 1)
            {
                if (needed > gaps[lo])
                    gaps[lo] = needed;
            }
            else
            {
                var perCut = Math.Clamp(pillW / (hi - lo) + 42.0, minGap, maxGap);
                for (var c = lo; c < hi; c++)
                {
                    if (perCut > gaps[c])
                        gaps[c] = perCut;
                }
            }
        }

        return gaps;
    }

    private static void OrderByBarycenter(
        IReadOnlyList<List<FlowNode>> groups,
        IReadOnlyList<FlowchartEdge> edges)
    {
        if (groups.Count < 2 || edges.Count == 0)
            return;

        var groupOf = new Dictionary<string, int>(StringComparer.Ordinal);
        var neighbors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        for (var g = 0; g < groups.Count; g++)
        {
            foreach (var n in groups[g])
            {
                groupOf[n.Id] = g;
                neighbors[n.Id] = new List<string>();
            }
        }

        foreach (var e in edges)
        {
            if (neighbors.TryGetValue(e.SourceId, out var srcList) &&
                neighbors.TryGetValue(e.TargetId, out var dstList) &&
                !string.Equals(e.SourceId, e.TargetId, StringComparison.Ordinal))
            {
                srcList.Add(e.TargetId);
                dstList.Add(e.SourceId);
            }
        }

        var normPos = new Dictionary<string, double>(StringComparer.Ordinal);
        void RefreshPositions(int gIdx)
        {
            var grp = groups[gIdx];
            var denom = Math.Max(1, grp.Count - 1);
            for (var i = 0; i < grp.Count; i++)
                normPos[grp[i].Id] = grp.Count == 1 ? 0.5 : (double)i / denom;
        }

        for (var g = 0; g < groups.Count; g++)
            RefreshPositions(g);

        void ReorderGroup(int gIdx, Func<int, bool> neighborFilter)
        {
            var grp = groups[gIdx];
            if (grp.Count <= 1)
                return;

            var scored = grp.Select((node, origIdx) =>
            {
                var nbrCoords = neighbors[node.Id]
                    .Where(id => groupOf.TryGetValue(id, out var ng) && neighborFilter(ng))
                    .Select(id => normPos[id])
                    .ToList();
                var key = nbrCoords.Count > 0
                    ? nbrCoords.Average()
                    : normPos[node.Id] + 0.0001 * origIdx;
                return (node, key, origIdx);
            })
            .OrderBy(x => x.key)
            .ThenBy(x => x.origIdx)
            .Select(x => x.node)
            .ToList();

            grp.Clear();
            grp.AddRange(scored);
            RefreshPositions(gIdx);
        }

        for (var sweep = 0; sweep < 2; sweep++)
        {
            for (var g = 1; g < groups.Count; g++)
            {
                var cur = g;
                ReorderGroup(g, ng => ng < cur);
            }

            for (var g = groups.Count - 2; g >= 0; g--)
            {
                var cur = g;
                ReorderGroup(g, ng => ng > cur);
            }
        }
    }

    private static Dictionary<string, int> ComputeRanks(
        List<FlowNode> nodeOrder,
        List<FlowchartEdge> edges)
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
