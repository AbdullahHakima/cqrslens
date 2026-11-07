using System.Text.Json.Serialization;

namespace CqrsLens;

/// <summary>
/// Endpoints added, removed or unchanged between the base and head scans. The CLI's architecture
/// diff JSON/Markdown does not render endpoints (its surface is the CQRS shape kinds), so this
/// section is <see cref="JsonIgnoreAttribute">ignored</see> by the diff serializer and never
/// affects <c>hasChanges</c>: it exists for the MCP diff answer, computed from the same two report
/// documents through the same calculator.
/// </summary>
internal sealed class EndpointDiffSection
{
    public List<JsonEndpoint> Added { get; set; } = new();
    public List<JsonEndpoint> Removed { get; set; } = new();

    /// <summary>Number of endpoint identities present in both scans.</summary>
    [JsonIgnore]
    public int Unchanged { get; set; }

    [JsonIgnore]
    public bool HasChanges => Added.Count > 0 || Removed.Count > 0;
}
