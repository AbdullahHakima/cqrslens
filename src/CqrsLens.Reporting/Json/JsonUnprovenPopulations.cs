namespace CqrsLens;

internal sealed class JsonUnprovenPopulations
{
    public int TotalCount { get; set; }
    public List<JsonUnresolvedDispatch>? UnresolvedDispatches { get; set; }
    public List<JsonAmbiguousResolution>? AmbiguousResolutions { get; set; }
    public List<JsonUnprovenDiagnostic>? AmbiguousLinks { get; set; }
    public List<JsonUnprovenDiagnostic>? UnresolvedContracts { get; set; }
    public List<JsonUnestablishedBehavior>? UnestablishedBehaviors { get; set; }
    public List<JsonUnprovenDiagnostic>? Observations { get; set; }
}
