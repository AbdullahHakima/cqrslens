namespace CqrsLens;

/// <summary>On-disk baseline document. Kept small and deterministic so it is reviewable in git.</summary>
public sealed class BaselineDocument
{
    public int? SchemaVersion { get; set; }

    public List<BaselineEntry> Findings { get; set; } = new();
}
