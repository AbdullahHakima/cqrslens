using System.Text.Json.Nodes;

namespace CqrsLens;

/// <summary>Reads the versioned design format used by the CLI and MCP.</summary>
internal static class SliceDesignInput
{
    public static JsonObject Schema()
    {
        using var stream = typeof(SliceDesignInput).Assembly.GetManifestResourceStream("CqrsLens.SliceDesignSchema")
            ?? throw new InvalidOperationException("The slice design schema is missing from the tool package.");
        return JsonNode.Parse(stream)!.AsObject();
    }

    public static SliceDesign Read(JsonObject input)
    {
        if (input["schemaVersion"] is not JsonValue version || !version.TryGetValue<int>(out var number) ||
            number != SliceDesign.CurrentSchemaVersion)
            throw new ArgumentException($"'schemaVersion' must be the supported slice design version, {SliceDesign.CurrentSchemaVersion}.");

        ModelJson.ValidateKeys(
            input,
            "slice design",
            "$schema",
            "schemaVersion",
            "project",
            "request",
            "kind",
            "responseType",
            "handler",
            "validator",
            "endpoint",
            "scenarios",
            "properties",
            "publishes",
            "dispatches",
            "persistence",
            "dependencies",
            "forbiddenDependencies");
        _ = Optional(input, "$schema");
        if (!input.TryGetPropertyValue("responseType", out var responseType))
            throw new ArgumentException("'responseType' is required. Use null for a request without a response.");

        var design = new SliceDesign(
            number,
            Required(input, "project"),
            Required(input, "request"),
            Required(input, "kind"),
            responseType is null ? null : Required(input, "responseType"),
            Optional(input, "handler"),
            Optional(input, "validator"),
            ReadEndpoint(input),
            ReadScenarios(input),
            ReadProperties(input),
            ReadStringArray(input, "publishes"),
            ReadStringArray(input, "dispatches"),
            ReadPersistence(input),
            ReadStringArray(input, "dependencies"),
            ReadStringArray(input, "forbiddenDependencies"));
        SliceDesignEvaluator.Validate(design);
        return design;
    }

    private static SliceDesignEndpoint? ReadEndpoint(JsonObject input)
    {
        if (!input.TryGetPropertyValue("endpoint", out var value) || value is null)
            return null;
        if (value is not JsonObject endpoint)
            throw new ArgumentException("'endpoint' must be an object.");

        ModelJson.ValidateKeys(endpoint, "endpoint", "method", "route");
        return new SliceDesignEndpoint(Required(endpoint, "method", "endpoint."), Required(endpoint, "route", "endpoint."));
    }

    private static IReadOnlyList<SliceDesignScenario> ReadScenarios(JsonObject input)
    {
        const int max = SliceDesign.MaxScenarios;
        if (input["scenarios"] is not JsonArray scenarios || scenarios.Count is < 1 or > max)
            throw new ArgumentException($"'scenarios' must contain between 1 and {max} Given/When/Then scenarios.");

        var result = new List<SliceDesignScenario>(scenarios.Count);
        for (var i = 0; i < scenarios.Count; i++)
        {
            var path = $"scenarios[{i}]";
            if (scenarios[i] is not JsonObject item)
                throw new ArgumentException($"'{path}' must be an object.");

            ModelJson.ValidateKeys(item, path, "name", "given", "when", "then");
            result.Add(new SliceDesignScenario(Required(item, "name", path + "."), Required(item, "given", path + "."),
                Required(item, "when", path + "."), Required(item, "then", path + ".")));
        }
        return result;
    }

    private static IReadOnlyList<SliceDesignProperty>? ReadProperties(JsonObject input)
    {
        if (!input.TryGetPropertyValue("properties", out var value) || value is null)
            return null;
        if (value is not JsonArray properties)
            throw new ArgumentException("'properties' must be an array.");

        var result = new List<SliceDesignProperty>(properties.Count);
        for (var i = 0; i < properties.Count; i++)
        {
            var path = $"properties[{i}]";
            if (properties[i] is not JsonObject item)
                throw new ArgumentException($"'{path}' must be an object.");

            ModelJson.ValidateKeys(item, path, "name", "type");
            result.Add(new SliceDesignProperty(
                Required(item, "name", path + "."),
                Optional(item, "type", path + ".")));
        }

        return result;
    }

    private static SliceDesignPersistence? ReadPersistence(JsonObject input)
    {
        if (!input.TryGetPropertyValue("persistence", out var value) || value is null)
            return null;
        if (value is not JsonObject persistence)
            throw new ArgumentException("'persistence' must be an object.");

        ModelJson.ValidateKeys(persistence, "persistence", "dbContexts", "entities");
        return new SliceDesignPersistence(
            ReadStringArray(persistence, "dbContexts", "persistence."),
            ReadStringArray(persistence, "entities", "persistence."));
    }

    private static IReadOnlyList<string>? ReadStringArray(JsonObject input, string field, string prefix = "")
    {
        if (!input.TryGetPropertyValue(field, out var value) || value is null)
            return null;
        if (value is not JsonArray array)
            throw new ArgumentException($"'{prefix}{field}' must be an array.");

        var result = new List<string>(array.Count);
        for (var i = 0; i < array.Count; i++)
        {
            if (array[i] is JsonValue json && json.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
            {
                result.Add(text);
                continue;
            }

            throw new ArgumentException($"'{prefix}{field}[{i}]' must be a non-empty string.");
        }

        return result;
    }

    private static string? Optional(JsonObject input, string field, string prefix = "")
        => input.TryGetPropertyValue(field, out var value) && value is not null
            ? Required(input, field, prefix)
            : null;

    private static string Required(JsonObject input, string field, string prefix = "")
    {
        if (input[field] is JsonValue json && json.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
            return text;

        throw new ArgumentException($"'{prefix}{field}' must be a non-empty string.");
    }
}
