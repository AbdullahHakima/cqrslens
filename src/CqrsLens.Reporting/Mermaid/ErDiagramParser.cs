namespace CqrsLens;

/// <summary>Parses Mermaid ER diagram source lines into an <see cref="ErDiagramModel"/>.</summary>
internal static class ErDiagramParser
{
    internal static ErDiagramModel Parse(string[] rawLines)
    {
        var model = new ErDiagramModel();
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
                model.AccTitle = MermaidEntityDecoder.Decode(line["accTitle:".Length..].Trim());
                continue;
            }

            if (line.StartsWith("accDescr:", StringComparison.Ordinal))
            {
                model.AccDescr = MermaidEntityDecoder.Decode(line["accDescr:".Length..].Trim());
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

            if (TryParseRelationship(line, out var relationship))
            {
                model.Relationships.Add(relationship!);
                continue;
            }

            var opensBlock = line.EndsWith('{');
            var headerPart = opensBlock ? line[..^1].Trim() : line;
            MermaidNodeSyntax.ParseIdAndLabel(headerPart, out var entId, out var entLabel, out _, out _);
            var decodedLabel = MermaidEntityDecoder.Decode(entLabel);
            if (!model.EntityById.TryGetValue(entId, out var entity))
            {
                entity = new ErEntityBox(entId, decodedLabel);
                model.EntityById[entId] = entity;
                model.Entities.Add(entity);
            }

            if (opensBlock)
                currentEntity = entity;
        }

        return model;
    }

    private static bool TryParseRelationship(string line, out ErRelationshipEdge? edge)
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
                MermaidEntityDecoder.Decode(rawLabel));
            return true;
        }

        return false;
    }
}
