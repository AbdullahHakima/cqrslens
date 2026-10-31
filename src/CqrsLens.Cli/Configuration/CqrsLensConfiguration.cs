using System.Text.Json.Serialization;

namespace CqrsLens;

/// <summary>The on-disk .cqrslens.json shape. Validation happens in <see cref="ConfigurationValidator"/>.</summary>
internal sealed class CqrsLensConfiguration
{
    [JsonPropertyName("$schema")]
    public string? Schema { get; set; }

    public string? Analysis { get; set; }
    public string? Render { get; set; }
    public string? Watermark { get; set; }
    public List<string>? Markers { get; set; }
    public List<string>? HandlerMarkers { get; set; }
    public List<string>? Include { get; set; }
    public List<string>? Exclude { get; set; }
    public Dictionary<string, string>? Layers { get; set; }
    public Dictionary<string, string>? Rules { get; set; }
}
