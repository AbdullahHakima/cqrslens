namespace CqrsLens;

/// <summary>A subgraph band that groups flowchart nodes.</summary>
internal sealed class FlowSubgraph
{
    public FlowSubgraph(string id, string label)
    {
        Id = id;
        Label = label;
    }

    public string Id { get; }
    public string Label { get; }
    public List<FlowNode> Nodes { get; } = new();
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}
