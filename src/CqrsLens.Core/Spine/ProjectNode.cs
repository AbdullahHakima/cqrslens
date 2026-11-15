namespace CqrsLens;

/// <summary>
/// Structural project container node in the spine, carrying its <see cref="ProjectId"/>,
/// layer assignment, and explicit configured-vs-guessed provenance (F0 §3, §6).
/// </summary>
public sealed class ProjectNode
{
    public ProjectId Id { get; }
    public string Name { get; }
    public string FilePath { get; }
    public string DirectoryPath { get; }
    public string Layer { get; }
    public LayerProvenance LayerProvenance { get; }
    public EvidenceStrength LayerStrength =>
        LayerProvenance == LayerProvenance.Configured ? EvidenceStrength.Proven : EvidenceStrength.Inferred;
    public IReadOnlyList<string> PackageReferences { get; }
    public FactProvenance LayerFactProvenance { get; }

    public ProjectNode(
        ProjectId id,
        string name,
        string filePath,
        string directoryPath,
        string layer,
        LayerProvenance layerProvenance,
        IReadOnlyList<string>? packageReferences = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(layer);

        Id = id;
        Name = name.Trim();
        FilePath = filePath;
        DirectoryPath = directoryPath;
        Layer = layer.Trim();
        LayerProvenance = layerProvenance;
        PackageReferences = packageReferences is null || packageReferences.Count == 0
            ? Array.Empty<string>()
            : packageReferences.ToArray();
        LayerFactProvenance = new FactProvenance(
            "Layer",
            LayerStrength,
            layerProvenance == LayerProvenance.Configured ? "Configured layer override" : "Name/folder convention guess");
    }
}
