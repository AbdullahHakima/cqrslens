namespace CqrsLens;

internal sealed class JsonProject
{
    public string Name { get; set; } = "";
    public string Layer { get; set; } = "";
    public List<string> References { get; set; } = new();
    public List<string> Packages { get; set; } = new();
}
