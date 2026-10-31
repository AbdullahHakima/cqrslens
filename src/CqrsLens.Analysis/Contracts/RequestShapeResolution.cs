namespace CqrsLens;

/// <summary>Result of following a class's base classes/interfaces for request roots.</summary>
internal sealed class RequestShapeResolution
{
    public List<string> Roots { get; } = new();
    public string? ResponseType { get; set; }
    public List<string> AmbiguousCandidates { get; } = new();
    public List<string> UnresolvedBases { get; } = new();
}
