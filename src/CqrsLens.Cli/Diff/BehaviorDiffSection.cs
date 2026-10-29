using System.Text.Json.Serialization;

using static CqrsLens.JsonReportGenerator;

namespace CqrsLens;

/// <summary>Pipeline behaviors added or removed between the base and head scans.</summary>
internal sealed class BehaviorDiffSection
{
    public List<JsonBehavior> Added { get; set; } = new();
    public List<JsonBehavior> Removed { get; set; } = new();

    [JsonIgnore]
    public bool HasChanges => Added.Count > 0 || Removed.Count > 0;
}
