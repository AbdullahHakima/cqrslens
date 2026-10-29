namespace CqrsLens;

/// <summary>Diagram rendering mode for HTML architecture reports.</summary>
public enum DiagramRenderMode
{
    /// <summary>Emit <c>&lt;pre class="mermaid"&gt;</c> blocks rendered in the browser by Mermaid.js.</summary>
    Mermaid,

    /// <summary>Compile diagrams to inline <c>&lt;svg&gt;</c> ahead of time so the HTML report requires zero JavaScript.</summary>
    Svg,
}

/// <summary>
/// Deterministic, zero-dependency .NET renderer that compiles CqrsLens Mermaid
/// diagrams (<c>flowchart TD</c>, <c>flowchart LR</c>, and <c>erDiagram</c>) to
/// self-contained inline SVG.
/// </summary>
public static class MermaidSvgRenderer
{
    public static string Render(string mermaidCode)
    {
        ArgumentNullException.ThrowIfNull(mermaidCode);

        var lines = mermaidCode
            .ReplaceLineEndings("\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        var firstLine = lines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim() ?? "";
        return firstLine.StartsWith("erDiagram", StringComparison.Ordinal)
            ? MermaidErRenderer.Render(lines)
            : MermaidFlowchartRenderer.Render(lines);
    }

    internal static string DecodeMermaidEntities(string? value)
        => MermaidEntityDecoder.Decode(value);
}
