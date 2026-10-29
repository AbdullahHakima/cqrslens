using System.Text;
using static CqrsLens.MermaidEntityDecoder;
using static CqrsLens.MermaidSvgText;

namespace CqrsLens;

/// <summary>Parses and renders Mermaid ER diagrams to deterministic inline SVG.</summary>
internal static class MermaidErRenderer
{
    private static string E(string? value) => MermaidSvgText.Encode(value);

    private static string F(double value) => MermaidSvgText.Format(value);

    private static string DecodeMermaidEntities(string? value) => MermaidEntityDecoder.Decode(value);

    internal static string Render(string[] rawLines)
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
            MermaidFlowchartRenderer.ParseIdAndLabel(headerPart, out var entId, out var entLabel, out _, out _);
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