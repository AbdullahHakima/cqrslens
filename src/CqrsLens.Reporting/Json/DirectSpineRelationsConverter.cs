using System.Text.Json;
using System.Text.Json.Serialization;

namespace CqrsLens;

/// <summary>
/// Streams <see cref="SpineRelation"/> instances directly into <see cref="Utf8JsonWriter"/>
/// during report serialization while preserving standard list deserialization and DTO paths.
/// </summary>
internal sealed class DirectSpineRelationsConverter : JsonConverter<List<JsonSpineRelation>>
{
    public override List<JsonSpineRelation>? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        return JsonSerializer.Deserialize(ref reader, typeof(List<JsonSpineRelation>), options) as List<JsonSpineRelation>;
    }

    public override void Write(
        Utf8JsonWriter writer,
        List<JsonSpineRelation> value,
        JsonSerializerOptions options)
    {
        if (value is DirectSpineRelationPlaceholder direct)
        {
            WriteSpineRelations(writer, direct.SpineRelations);
            return;
        }

        JsonSerializer.Serialize(writer, value, JsonReportSerializerContext.Default.ListJsonSpineRelation);
    }

    internal static void WriteSpineRelations(Utf8JsonWriter writer, IReadOnlyList<SpineRelation> relations)
    {
        writer.WriteStartArray();
        for (var i = 0; i < relations.Count; i++)
            WriteRelation(writer, relations[i]);
        writer.WriteEndArray();
    }

    private static void WriteRelation(Utf8JsonWriter writer, SpineRelation relation)
    {
        writer.WriteStartObject();
        writer.WriteString("kind"u8, JsonSpineNames.RelationKindUtf8(relation.Kind));
        writer.WritePropertyName("from"u8);
        WriteNodeRef(writer, relation.From);
        writer.WritePropertyName("to"u8);
        WriteNodeRef(writer, relation.To);

        var via = relation.Via;
        if (via.Count > 0)
        {
            writer.WriteStartArray("via"u8);
            for (var i = 0; i < via.Count; i++)
                WriteNodeRef(writer, via[i]);
            writer.WriteEndArray();
        }

        if (!string.IsNullOrEmpty(relation.Method))
            writer.WriteString("method"u8, relation.Method);

        writer.WriteString("strength"u8, JsonSpineNames.EvidenceStrengthUtf8(relation.Strength));
        writer.WriteNumber("occurrenceCount"u8, relation.OccurrenceCount);

        writer.WriteStartArray("occurrences"u8);
        var occurrences = relation.Occurrences;
        for (var i = 0; i < occurrences.Count; i++)
            WriteEvidence(writer, occurrences[i]);
        writer.WriteEndArray();

        writer.WriteEndObject();
    }

    private static void WriteNodeRef(Utf8JsonWriter writer, NodeReference reference)
    {
        writer.WriteStartObject();
        writer.WriteString("kind"u8, JsonSpineNames.NodeReferenceKindUtf8(reference.Kind));
        if (reference.Key is not null)
            writer.WriteString("key"u8, reference.Key);
        if (reference.Name is not null)
            writer.WriteString("name"u8, reference.Name);
        if (reference.ComponentId is { } componentId)
            writer.WriteString("componentId"u8, componentId.Value);
        if (reference.EndpointId is { } endpointId)
            writer.WriteString("endpointId"u8, endpointId.ToString());
        if (reference.ProjectId is { } projectId)
            writer.WriteString("projectId"u8, projectId.Value);
        if (reference.SymbolId is { } symbolId)
            writer.WriteString("symbolId"u8, symbolId.ToString());
        if (reference.MemberName is not null)
            writer.WriteString("member"u8, reference.MemberName);
        if (reference.Reason is not null)
            writer.WriteString("reason"u8, reference.Reason);
        writer.WriteEndObject();
    }

    internal static void WriteEvidence(Utf8JsonWriter writer, EvidenceOccurrence occurrence)
    {
        writer.WriteStartObject();
        writer.WriteString("rule"u8, JsonSpineNames.EvidenceRuleUtf8(occurrence.Rule));
        writer.WriteString("origin"u8, JsonSpineNames.EvidenceOriginUtf8(occurrence.Origin));
        writer.WriteString("strength"u8, JsonSpineNames.EvidenceStrengthUtf8(occurrence.Strength));

        if (occurrence.Location is { } location)
        {
            writer.WriteStartObject("location"u8);
            if (location.File is not null)
                writer.WriteString("file"u8, location.File);
            writer.WriteNumber("line"u8, location.Line);
            writer.WriteNumber("column"u8, location.Column);
            writer.WriteEndObject();
        }

        if (occurrence.LocationOmittedReason is not null)
            writer.WriteString("locationOmittedReason"u8, occurrence.LocationOmittedReason);

        var via = occurrence.Via;
        if (via.Count > 0)
        {
            writer.WriteStartArray("via"u8);
            for (var i = 0; i < via.Count; i++)
                WriteNodeRef(writer, via[i]);
            writer.WriteEndArray();
        }

        if (occurrence.ResolutionSemantics is { } semantics)
            writer.WriteString("resolutionSemantics"u8, JsonSpineNames.ResolutionSemanticsUtf8(semantics));

        if (occurrence.ScopeProjectName is not null)
            writer.WriteString("scopeProject"u8, occurrence.ScopeProjectName);

        if (occurrence.IsTestProject)
            writer.WriteBoolean("testProject"u8, true);

        if (occurrence.Detail is not null)
            writer.WriteString("detail"u8, occurrence.Detail);

        writer.WriteEndObject();
    }
}
