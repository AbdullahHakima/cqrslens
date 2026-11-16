namespace CqrsLens;

/// <summary>On-disk intended public surface declaration under <c>intent.publicSurface</c>.</summary>
internal sealed class IntentPublicSurfaceConfiguration
{
    public List<string>? Endpoints { get; set; }
    public List<string>? Components { get; set; }
    public string? Severity { get; set; }
}
