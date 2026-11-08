namespace CqrsLens;

/// <summary>
/// One bounded flow diagram (endpoint → request → handler → DbContext/notification)
/// and the flow edges it covers. Every flow edge appears in exactly one section.
/// </summary>
public sealed record FlowDiagramSection(
    string? Label,
    string Source,
    IReadOnlyList<FlowEdge> Edges,
    int Part,
    int PartCount,
    int OwnerCount)
{
    /// <summary>
    /// DI-resolved edges drawn in this section (today handler → DbContext through an
    /// injected interface). Additive: <see cref="Edges"/> still holds only direct flow
    /// edges, so every existing count and surface is unchanged.
    /// </summary>
    public IReadOnlyList<ResolvedEdge> ResolvedEdges { get; init; } = Array.Empty<ResolvedEdge>();

    public string Heading => Label is null
        ? PartCount > 1
            ? $"Flow — part {Part} of {PartCount}"
            : "Flow"
        : PartCount > 1
            ? $"{Label} — flow (part {Part} of {PartCount})"
            : $"{Label} — flow";

    public string Caption
    {
        get
        {
            var nouns = new List<string> { Count(Edges.Count, "edge") };
            if (OwnerCount > 0)
                nouns.Add(Count(OwnerCount, "connected flow"));
            return string.Join(" · ", nouns);
        }
    }

    private static string Count(int count, string noun)
        => count == 1 ? $"1 {noun}" : $"{count} {noun}s";
}
