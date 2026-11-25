using System.Text.Json.Nodes;

namespace CqrsLens;

/// <summary>Reads the architecture proposal arguments shared by the CLI and MCP.</summary>
internal sealed record ArchitectureProposalInput(
    IReadOnlyList<ArchitectureProposalProject> Projects,
    IReadOnlyList<ArchitectureProposalReference> References)
{
    public static ArchitectureProposalInput Read(JsonObject input)
    {
        var projects = ReadItems(input, "projects", ReadProject);
        var references = ReadItems(input, "references", ReadReference);
        if (projects.Count == 0 && references.Count == 0)
            throw new ArgumentException("Provide at least one proposed project or reference change.");

        return new ArchitectureProposalInput(projects, references);
    }

    private static ArchitectureProposalProject ReadProject(JsonObject input, string label)
    {
        ModelJson.ValidateKeys(input, label, "name", "layer");
        return new ArchitectureProposalProject(ReadString(input, label, "name"), ReadString(input, label, "layer"));
    }

    private static ArchitectureProposalReference ReadReference(JsonObject input, string label)
    {
        ModelJson.ValidateKeys(input, label, "from", "to", "operation");
        return new ArchitectureProposalReference(
            ReadString(input, label, "from"), ReadString(input, label, "to"),
            ReadString(input, label, "operation", "add"));
    }

    private static IReadOnlyList<T> ReadItems<T>(JsonObject input, string key, Func<JsonObject, string, T> read)
    {
        if (!input.TryGetPropertyValue(key, out var value))
            return [];

        if (value is not JsonArray array)
            throw new ArgumentException($"'{key}' must be an array.");

        var items = new List<T>(array.Count);
        for (var i = 0; i < array.Count; i++)
        {
            var label = $"{key}[{i}]";
            if (array[i] is not JsonObject item)
                throw new ArgumentException($"'{label}' must be an object.");

            items.Add(read(item, label));
        }

        return items;
    }

    private static string ReadString(JsonObject input, string label, string key, string? fallback = null)
    {
        if (!input.TryGetPropertyValue(key, out var value) && fallback is not null)
            return fallback;

        if (value is JsonValue json && json.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
            return text.Trim();

        throw new ArgumentException($"'{label}.{key}' must be a non-empty string.");
    }
}
