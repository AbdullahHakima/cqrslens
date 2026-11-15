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

    public static string Generate(AnalysisResult result)
    {
        var document = JsonReportDocumentFactory.CreateForSerialization(result);
        var estimatedBytes = Math.Clamp(16384 + result.Spine.UnindexedRelations.Count * 1600, 16384, 8 * 1024 * 1024);
        using var buffer = new PooledByteBufferWriter(estimatedBytes);
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            JsonSerializer.Serialize(writer, document, JsonReportSerializerContext.Default.JsonReportDocument);
        }
        buffer.AppendByte((byte)'\n');
        return buffer.ToUtf8String();
    }

    internal static JsonReportDocument CreateDocument(AnalysisResult result)
        => JsonReportDocumentFactory.Create(result);

    internal static JsonReportDocument ReadDocument(string path)
        => JsonReportDocumentReader.Read(path);
}
