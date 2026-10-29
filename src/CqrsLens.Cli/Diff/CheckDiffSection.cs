using System.Text.Json.Serialization;

namespace CqrsLens;

/// <summary>Architecture check findings introduced or resolved by the head scan.</summary>
internal sealed class CheckDiffSection
{
    public List<JsonCheckFinding> NewFindings { get; set; } = new();
    public List<JsonCheckFinding> ResolvedFindings { get; set; } = new();

    [JsonIgnore]
    public bool HasChanges => NewFindings.Count > 0 || ResolvedFindings.Count > 0;
}
