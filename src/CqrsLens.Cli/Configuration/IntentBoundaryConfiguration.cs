namespace CqrsLens;

/// <summary>On-disk forbidden module call boundary under <c>intent.boundaries</c>.</summary>
internal sealed class IntentBoundaryConfiguration
{
    public string? From { get; set; }
    public string? To { get; set; }
    public string? Severity { get; set; }
}
