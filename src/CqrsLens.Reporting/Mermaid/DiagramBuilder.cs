namespace CqrsLens;

/// <summary>Builds the Mermaid diagram sources shared by the Markdown and HTML reports.</summary>
public static class DiagramBuilder
{
    /// <summary>
    /// Maximum requests in one CQRS diagram. The accurate request-only subgraph
    /// layout is roughly 100 px per request on GitHub; 15 keeps each part under
    /// about 1,600 px while staying far below Mermaid's 50,000-character limit.
    /// </summary>
    internal const int MaxRequestsPerCqrsDiagram = CqrsDiagramBuilder.MaxRequestsPerCqrsDiagram;

    /// <summary>
    /// Makes an ASCII-only, Mermaid-safe base id from an arbitrary name. Reserved
    /// words are prefixed with <c>n_</c>; diagram builders add role prefixes.
    /// </summary>
    public static string MermaidId(string? name) => MermaidDiagramText.MermaidId(name);

    /// <summary>
    /// Escapes text used inside a quoted Mermaid label. GitHub HTML-decodes its
    /// input before Mermaid sees it, so HTML entities such as <c>&amp;quot;</c>
    /// are unsafe. Mermaid's own <c>#...;</c> entities are decoded exactly once.
    /// The label must not be able to terminate its quote or inject a new line.
    /// </summary>
    public static string MermaidLabel(string? value) => MermaidDiagramText.MermaidLabel(value);

    public static string? ProjectGraph(AnalysisResult result) => ProjectDiagramBuilder.ProjectGraph(result);

    public static string? LayerGraph(AnalysisResult result) => ProjectDiagramBuilder.LayerGraph(result);

    /// <summary>
    /// Legacy single-diagram helper. Prefer <see cref="CqrsMapSections"/> for
    /// reports: large solutions are split into multiple bounded diagrams.
    /// </summary>
    public static string? CqrsMap(AnalysisResult result) => CqrsDiagramBuilder.CqrsMap(result);

    /// <summary>
    /// Builds one CQRS map for small solutions, or splits larger solutions by
    /// project and then into balanced parts. Every request appears exactly once.
    /// </summary>
    public static IReadOnlyList<DiagramSection> CqrsMapSections(AnalysisResult result)
        => CqrsDiagramBuilder.CqrsMapSections(result);

    /// <summary>Whether a Mermaid source is safely inside GitHub's renderer limits.</summary>
    internal static bool IsWithinMermaidLimits(string source)
        => MermaidDiagramText.IsWithinMermaidLimits(source);

    /// <summary>
    /// Builds a Mermaid <c>flowchart LR</c> fan-out diagram for discovered notifications
    /// (domain events) and their notification handlers, or returns <see langword="null"/>
    /// when no notifications were discovered.
    /// </summary>
    public static string? NotificationMap(AnalysisResult result) => NotificationDiagramBuilder.NotificationMap(result);

    /// <summary>
    /// Builds a Mermaid <c>erDiagram</c> for one <see cref="DbContextInfo"/>,
    /// or returns <see langword="null"/> when the context has no discovered entities.
    /// </summary>
    public static string? ErDiagram(DbContextInfo context) => ErDiagramBuilder.ErDiagram(context);
}
