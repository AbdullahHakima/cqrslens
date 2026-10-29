namespace CqrsLens;

/// <summary>A node in a parsed Mermaid flowchart.</summary>
internal sealed class FlowNode
{
    public FlowNode(string id, string label)
    {
        Id = id;
        Label = label;
    }

    public string Id { get; }
    public string Label { get; set; }
    public bool IsStadium { get; set; }
    public string? ClassName { get; set; }
    public FlowSubgraph? Subgraph { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}
