namespace CqrsLens;

/// <summary>Raw command-line values before configuration and defaults are merged.</summary>
public sealed class CliOptions
{
    public string? Path { get; set; }
    public string? Output { get; set; }
    public string? Format { get; set; }
    public string? SliceFilter { get; set; }
    public DiagramRenderMode RenderMode { get; set; } = DiagramRenderMode.Mermaid;
    public bool RenderModeSpecified { get; set; }
    public string? ConfigPath { get; set; }
    public string? BaselinePath { get; set; }
    public string? WriteBaselinePath { get; set; }
    public string? DiffPath { get; set; }
    public string? DiffOutputPath { get; set; }
    public string? SarifPath { get; set; }
    public string? SummaryPath { get; set; }
    public string? CiRoot { get; set; }
    public bool GitHubAnnotations { get; set; }
    public AnalysisMode AnalysisMode { get; set; } = AnalysisMode.Text;
    public bool AnalysisModeSpecified { get; set; }
    public DateTimeOffset? GeneratedAt { get; set; }
    public bool Strict { get; set; }
    public bool MarkersSpecified { get; set; }
    public HashSet<string> Markers { get; } = new(StringComparer.Ordinal);
    public bool HandlerMarkersSpecified { get; set; }
    public HashSet<string> HandlerMarkers { get; } = new(StringComparer.Ordinal);
    public string? Watermark { get; set; }
    public bool WatermarkSpecified { get; set; }
    public bool? Banner { get; set; }
    public bool? Color { get; set; }
    public bool Mcp { get; set; }
    public bool Profile { get; set; }
}
