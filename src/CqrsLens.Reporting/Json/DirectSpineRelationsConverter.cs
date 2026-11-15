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

    private static void WriteSpineRelations(Utf8JsonWriter writer, IReadOnlyList<SpineRelation> relations)
    {
        writer.WriteStartArray();
        for (var i = 0; i < relations.Count; i++)
            WriteRelation(writer, relations[i]);
        writer.WriteEndArray();
    }

    private static void WriteRelation(Utf8JsonWriter writer, SpineRelation relation)
    {
        writer.WriteStartObject();
        writer.WriteString(JsonSpineNames.KindProp, JsonSpineNames.RelationKindEncoded(relation.Kind));
        writer.WritePropertyName(JsonSpineNames.FromProp);
        WriteNodeRef(writer, relation.From);
        writer.WritePropertyName(JsonSpineNames.ToProp);
        WriteNodeRef(writer, relation.To);

        var via = relation.Via;
        if (via.Count > 0)
        {
            writer.WriteStartArray(JsonSpineNames.ViaProp);
            for (var i = 0; i < via.Count; i++)
                WriteNodeRef(writer, via[i]);
            writer.WriteEndArray();
        }

        if (!string.IsNullOrEmpty(relation.Method))
            writer.WriteString(JsonSpineNames.MethodProp, relation.Method);

        writer.WriteString(JsonSpineNames.StrengthProp, JsonSpineNames.EvidenceStrengthEncoded(relation.Strength));
        writer.WriteNumber(JsonSpineNames.OccurrenceCountProp, relation.OccurrenceCount);

        writer.WriteStartArray(JsonSpineNames.OccurrencesProp);
        var occurrences = relation.Occurrences;
        for (var i = 0; i < occurrences.Count; i++)
            WriteEvidence(writer, occurrences[i]);
        writer.WriteEndArray();

        writer.WriteEndObject();
    }

    private static void WriteNodeRef(Utf8JsonWriter writer, NodeReference reference)
    {
        writer.WriteStartObject();
        writer.WriteString(JsonSpineNames.KindProp, JsonSpineNames.NodeReferenceKindEncoded(reference.Kind));
        if (reference.Key is not null)
            writer.WriteString(JsonSpineNames.KeyProp, reference.Key);
        if (reference.Name is not null)
            writer.WriteString(JsonSpineNames.NameProp, reference.Name);
        if (reference.ComponentId is { } componentId)
            writer.WriteString(JsonSpineNames.ComponentIdProp, componentId.Value);
        if (reference.EndpointId is { } endpointId)
            writer.WriteString(JsonSpineNames.EndpointIdProp, endpointId.ToString());
        if (reference.ProjectId is { } projectId)
            writer.WriteString(JsonSpineNames.ProjectIdProp, projectId.Value);
        if (reference.SymbolId is { } symbolId)
            writer.WriteString(JsonSpineNames.SymbolIdProp, symbolId.ToString());
        if (reference.MemberName is not null)
            writer.WriteString(JsonSpineNames.MemberProp, reference.MemberName);
        if (reference.Reason is not null)
            writer.WriteString(JsonSpineNames.ReasonProp, reference.Reason);
        writer.WriteEndObject();
    }

    private static void WriteEvidence(Utf8JsonWriter writer, EvidenceOccurrence occurrence)
    {
        writer.WriteStartObject();
        writer.WriteString(JsonSpineNames.RuleProp, JsonSpineNames.EvidenceRuleEncoded(occurrence.Rule));
        writer.WriteString(JsonSpineNames.OriginProp, JsonSpineNames.EvidenceOriginEncoded(occurrence.Origin));
        writer.WriteString(JsonSpineNames.StrengthProp, JsonSpineNames.EvidenceStrengthEncoded(occurrence.Strength));

        if (occurrence.Location is { } location)
        {
            writer.WriteStartObject(JsonSpineNames.LocationProp);
            if (location.File is not null)
                writer.WriteString(JsonSpineNames.FileProp, location.File);
            writer.WriteNumber(JsonSpineNames.LineProp, location.Line);
            writer.WriteNumber(JsonSpineNames.ColumnProp, location.Column);
            writer.WriteEndObject();
        }

        if (occurrence.LocationOmittedReason is not null)
            writer.WriteString(JsonSpineNames.LocationOmittedReasonProp, occurrence.LocationOmittedReason);

        var via = occurrence.Via;
        if (via.Count > 0)
        {
            writer.WriteStartArray(JsonSpineNames.ViaProp);
            for (var i = 0; i < via.Count; i++)
                WriteNodeRef(writer, via[i]);
            writer.WriteEndArray();
        }

        if (occurrence.ResolutionSemantics is { } semantics)
            writer.WriteString(JsonSpineNames.ResolutionSemanticsProp, JsonSpineNames.ResolutionSemanticsEncoded(semantics));

        if (occurrence.ScopeProjectName is not null)
            writer.WriteString(JsonSpineNames.ScopeProjectProp, occurrence.ScopeProjectName);

        if (occurrence.IsTestProject)
            writer.WriteBoolean(JsonSpineNames.TestProjectProp, true);

        writer.WriteEndObject();
    }
}
