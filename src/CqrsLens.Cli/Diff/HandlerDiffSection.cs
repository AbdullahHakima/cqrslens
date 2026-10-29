using System.Text.Json.Serialization;

namespace CqrsLens;

/// <summary>Handlers added or removed between the base and head scans.</summary>
internal sealed class HandlerDiffSection
{
    public List<JsonHandler> Added { get; set; } = new();
    public List<JsonHandler> Removed { get; set; } = new();

    [JsonIgnore]
    public bool HasChanges => Added.Count > 0 || Removed.Count > 0;
}
