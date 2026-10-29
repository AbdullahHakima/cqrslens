using System.Text.Json.Serialization;

using static CqrsLens.JsonReportGenerator;

namespace CqrsLens;

/// <summary>Requests added, removed or changed between the base and head scans.</summary>
internal sealed class RequestDiffSection
{
    public List<JsonRequest> Added { get; set; } = new();
    public List<JsonRequest> Removed { get; set; } = new();
    public List<ModifiedRequestDiff> Modified { get; set; } = new();

    [JsonIgnore]
    public bool HasChanges => Added.Count > 0 || Removed.Count > 0 || Modified.Count > 0;
}
