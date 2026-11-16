namespace CqrsLens;

/// <summary>On-disk <c>.cqrslens.json</c> <c>intent</c> section (F3 R-INTENT / R4).</summary>
internal sealed class IntentConfiguration
{
    public Dictionary<string, IntentModuleConfiguration>? Modules { get; set; }
    public List<IntentBoundaryConfiguration>? Boundaries { get; set; }
    public IntentPublicSurfaceConfiguration? PublicSurface { get; set; }
    public Dictionary<string, string>? Ownership { get; set; }
}
