using System.Text.Json;
using System.Text.Json.Serialization;

namespace CqrsLens;

/// <summary>
/// Generates a deterministic, machine-readable JSON architecture report
/// (<c>--format json</c>) for external dashboards, diffing tools, and custom
/// automation.
/// </summary>
public static class JsonReportGenerator
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Generate(AnalysisResult result)
    {
        var document = CreateDocument(result);
        return JsonSerializer.Serialize(document, WriteOptions) + "\n";
    }

    internal static JsonReportDocument CreateDocument(AnalysisResult result)
        => JsonReportDocumentFactory.Create(result);

    internal static JsonReportDocument ReadDocument(string path)
        => JsonReportDocumentReader.Read(path);
}
