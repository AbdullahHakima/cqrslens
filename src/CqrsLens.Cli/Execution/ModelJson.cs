using System.Text.Json.Nodes;

namespace CqrsLens;

internal static class ModelJson
{
    internal static void ValidateKeys(JsonObject input, string label, params string[] expected)
    {
        foreach (var property in input)
        {
            if (!expected.Contains(property.Key, StringComparer.Ordinal))
                throw new ArgumentException($"Unexpected field '{property.Key}' in {label}.");
        }
    }
}
