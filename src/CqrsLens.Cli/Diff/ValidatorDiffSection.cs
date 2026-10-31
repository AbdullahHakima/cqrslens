using System.Text.Json.Serialization;

namespace CqrsLens;

/// <summary>Validators added or removed between the base and head scans.</summary>
internal sealed class ValidatorDiffSection
{
    public List<JsonValidator> Added { get; set; } = new();
    public List<JsonValidator> Removed { get; set; } = new();

    [JsonIgnore]
    public bool HasChanges => Added.Count > 0 || Removed.Count > 0;
}
