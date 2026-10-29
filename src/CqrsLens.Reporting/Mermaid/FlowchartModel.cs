namespace CqrsLens;

/// <summary>The parsed shape of a Mermaid flowchart before layout.</summary>
internal sealed class FlowchartModel
{
    public bool IsLeftToRight { get; set; }
    public string? AccTitle { get; set; }
    public string? AccDescr { get; set; }
    public Dictionary<string, FlowNode> Nodes { get; } = new(StringComparer.Ordinal);
    public List<FlowNode> NodeOrder { get; } = new();
    public List<FlowSubgraph> Subgraphs { get; } = new();
    public List<FlowEdge> Edges { get; } = new();
    public Dictionary<string, StyleProps> ClassDefs { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, StyleProps> NodeStyles { get; } = new(StringComparer.Ordinal);
    public Dictionary<int, StyleProps> LinkStyles { get; } = new();
    public FlowSubgraph? CurrentSubgraph { get; set; }
}
