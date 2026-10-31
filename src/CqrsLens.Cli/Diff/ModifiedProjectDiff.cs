namespace CqrsLens;

/// <summary>A project whose layer, references or packages changed.</summary>
internal sealed class ModifiedProjectDiff
{
    public string Name { get; set; } = "";
    public string? OldLayer { get; set; }
    public string? NewLayer { get; set; }
    public List<string> AddedReferences { get; set; } = new();
    public List<string> RemovedReferences { get; set; } = new();
    public List<string> AddedPackages { get; set; } = new();
    public List<string> RemovedPackages { get; set; } = new();
}
