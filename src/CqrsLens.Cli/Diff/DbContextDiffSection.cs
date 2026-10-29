using System.Text.Json.Serialization;

using static CqrsLens.JsonReportGenerator;

namespace CqrsLens;

/// <summary>DbContexts added, removed or changed between the base and head scans.</summary>
internal sealed class DbContextDiffSection
{
    public List<JsonDbContext> Added { get; set; } = new();
    public List<JsonDbContext> Removed { get; set; } = new();
    public List<ModifiedDbContextDiff> Modified { get; set; } = new();

    [JsonIgnore]
    public bool HasChanges => Added.Count > 0 || Removed.Count > 0 || Modified.Count > 0;
}
