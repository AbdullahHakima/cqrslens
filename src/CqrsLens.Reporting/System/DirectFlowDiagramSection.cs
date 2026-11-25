namespace CqrsLens;

/// <summary>
/// One bounded per-project direct-flow diagram covering non-CQRS entrypoints
/// (HTTP direct endpoints, gRPC RPCs, background workers, and message consumers).
/// </summary>
public sealed record DirectFlowDiagramSection(
    string ProjectName,
    string Source,
    int EntrypointCount,
    int EdgeCount,
    int TotalEntrypointCount)
{
    /// <summary>Plain-text heading used by the Markdown and HTML reports.</summary>
    public string Heading => TotalEntrypointCount > EntrypointCount
        ? $"{ProjectName} — {EntrypointCount} of {TotalEntrypointCount} direct operations"
        : $"{ProjectName} — {MermaidDiagramText.CountText(EntrypointCount, "direct operation")}";

    /// <summary>Plain-text scope caption used below the diagram.</summary>
    public string Caption
    {
        get
        {
            var summary = $"{MermaidDiagramText.CountText(EntrypointCount, "operation")} · " +
                          $"{MermaidDiagramText.CountText(EdgeCount, "edge")}";
            return TotalEntrypointCount > EntrypointCount
                ? $"{summary} (top {EntrypointCount} of {TotalEntrypointCount} by dependency richness)"
                : summary;
        }
    }
}
