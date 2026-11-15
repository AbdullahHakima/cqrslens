using System.Text.Json.Serialization;

namespace CqrsLens;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(JsonReportDocument))]
[JsonSerializable(typeof(List<JsonSpineRelation>))]
internal sealed partial class JsonReportSerializerContext : JsonSerializerContext
{
}
