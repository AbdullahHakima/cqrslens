using System.Text.Json.Serialization;

using static CqrsLens.JsonReportGenerator;

namespace CqrsLens;

/// <summary>Projects added, removed or changed between the base and head scans.</summary>
internal sealed class ProjectDiffSection
{
    public List<JsonProject> Added { get; set; } = new();
    public List<JsonProject> Removed { get; set; } = new();
    public List<ModifiedProjectDiff> Modified { get; set; } = new();

    [JsonIgnore]
    public bool HasChanges => Added.Count > 0 || Removed.Count > 0 || Modified.Count > 0;
}
