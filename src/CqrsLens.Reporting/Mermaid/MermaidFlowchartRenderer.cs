namespace CqrsLens;

/// <summary>Parses, lays out and renders Mermaid flowcharts to deterministic inline SVG.</summary>
internal static class MermaidFlowchartRenderer
{
    internal static string Render(string[] rawLines)
    {
        var model = FlowchartParser.Parse(rawLines);
        var (canvasWidth, canvasHeight) = FlowchartLayout.Apply(model);
        return FlowchartSvgWriter.Write(model, canvasWidth, canvasHeight);
    }
}
