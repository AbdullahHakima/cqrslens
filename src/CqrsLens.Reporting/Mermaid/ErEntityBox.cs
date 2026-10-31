namespace CqrsLens;

/// <summary>A parsed ER entity and its laid-out box geometry.</summary>
internal sealed class ErEntityBox
{
    public ErEntityBox(string id, string label)
    {
        Id = id;
        Label = label;
    }

    public string Id { get; }
    public string Label { get; }
    public List<ErAttributeRow> Attributes { get; } = new();
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}
