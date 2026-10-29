using System.Globalization;

namespace CqrsLens;

/// <summary>Parses Mermaid flowchart source lines into a <see cref="FlowchartModel"/>.</summary>
internal static class FlowchartParser
{
    internal static FlowchartModel Parse(string[] rawLines)
    {
        var model = new FlowchartModel();

        foreach (var raw in rawLines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("%%", StringComparison.Ordinal))
                continue;

            if (line.StartsWith("flowchart ", StringComparison.Ordinal) ||
                line.StartsWith("graph ", StringComparison.Ordinal))
            {
                var dir = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "TD";
                model.IsLeftToRight = string.Equals(dir, "LR", StringComparison.OrdinalIgnoreCase) ||
                                      string.Equals(dir, "RL", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (line.StartsWith("accTitle:", StringComparison.Ordinal))
            {
                model.AccTitle = MermaidEntityDecoder.Decode(line["accTitle:".Length..].Trim());
                continue;
            }

            if (line.StartsWith("accDescr:", StringComparison.Ordinal))
            {
                model.AccDescr = MermaidEntityDecoder.Decode(line["accDescr:".Length..].Trim());
                continue;
            }

            if (line.StartsWith("subgraph ", StringComparison.Ordinal))
            {
                var rest = line["subgraph ".Length..].Trim();
                MermaidNodeSyntax.ParseIdAndLabel(rest, out var sgId, out var sgLabel, out _, out _);
                var sg = new FlowSubgraph(sgId, MermaidEntityDecoder.Decode(sgLabel));
                model.Subgraphs.Add(sg);
                model.CurrentSubgraph = sg;
                continue;
            }

            if (string.Equals(line, "end", StringComparison.Ordinal))
            {
                model.CurrentSubgraph = null;
                continue;
            }

            if (line.StartsWith("classDef ", StringComparison.Ordinal))
            {
                var rest = line["classDef ".Length..].Trim();
                var spaceIdx = rest.IndexOf(' ');
                if (spaceIdx > 0)
                {
                    var className = rest[..spaceIdx].Trim();
                    model.ClassDefs[className] = ParseStyleProps(rest[(spaceIdx + 1)..]);
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
                    model.NodeStyles[targetId] = ParseStyleProps(rest[(spaceIdx + 1)..]);
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
                    model.LinkStyles[edgeIdx] = ParseStyleProps(rest[(spaceIdx + 1)..]);
                }
                continue;
            }

            if (TryParseEdge(line, out var srcSpec, out var dstSpec, out var edgeLabel, out var isDotted))
            {
                var srcNode = EnsureNode(srcSpec, model);
                var dstNode = EnsureNode(dstSpec, model);
                model.Edges.Add(new FlowEdge(
                    srcNode.Id,
                    dstNode.Id,
                    edgeLabel is null ? null : MermaidEntityDecoder.Decode(edgeLabel),
                    isDotted));
                continue;
            }

            EnsureNode(line, model);
        }

        return model;
    }

    private static FlowNode EnsureNode(string spec, FlowchartModel model)
    {
        MermaidNodeSyntax.ParseIdAndLabel(spec, out var id, out var rawLabel, out var isStadium, out var className);
        var decodedLabel = MermaidEntityDecoder.Decode(rawLabel);

        if (!model.Nodes.TryGetValue(id, out var node))
        {
            node = new FlowNode(id, decodedLabel)
            {
                IsStadium = isStadium,
                ClassName = className,
                Subgraph = model.CurrentSubgraph,
            };
            model.Nodes[id] = node;
            model.NodeOrder.Add(node);
            model.CurrentSubgraph?.Nodes.Add(node);
        }
        else
        {
            if (!string.Equals(rawLabel, id, StringComparison.Ordinal))
                node.Label = decodedLabel;
            if (isStadium)
                node.IsStadium = true;
            if (className is not null)
                node.ClassName = className;
            if (node.Subgraph is null && model.CurrentSubgraph is not null)
            {
                node.Subgraph = model.CurrentSubgraph;
                model.CurrentSubgraph.Nodes.Add(node);
            }
        }

        return node;
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
}
