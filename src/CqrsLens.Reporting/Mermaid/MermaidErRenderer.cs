namespace CqrsLens;

/// <summary>Parses and renders Mermaid ER diagrams to deterministic inline SVG.</summary>
internal static class MermaidErRenderer
{
    internal static string Render(string[] rawLines)
    {
        var model = ErDiagramParser.Parse(rawLines);
        var (canvasWidth, canvasHeight) = ErDiagramLayout.Apply(model);
        return ErDiagramSvgWriter.Write(model, canvasWidth, canvasHeight);
    }
}
