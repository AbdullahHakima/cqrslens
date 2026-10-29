namespace CqrsLens;

/// <summary>Diagram rendering mode for HTML architecture reports.</summary>
public enum DiagramRenderMode
{
    /// <summary>Emit <c>&lt;pre class="mermaid"&gt;</c> blocks rendered in the browser by Mermaid.js.</summary>
    Mermaid,

    /// <summary>Compile diagrams to inline <c>&lt;svg&gt;</c> ahead of time so the HTML report requires zero JavaScript.</summary>
    Svg,
}
