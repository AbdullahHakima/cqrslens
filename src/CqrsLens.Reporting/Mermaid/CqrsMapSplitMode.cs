namespace CqrsLens;

/// <summary>
/// How the CQRS map is split into bounded diagrams.
/// <list type="bullet">
/// <item><see cref="Count"/> (default): group by project, then balance into
/// fixed-size parts.</item>
/// <item><see cref="Feature"/>: group requests by the feature segment of the
/// namespace they live in.</item>
/// <item><see cref="Unsplit"/>: one diagram per project, with no feature parts;
/// a project is only split further when its map exceeds the renderer's safety
/// limit.</item>
/// </list>
/// The mode is selected with <c>--cqrs-map-split</c> or the <c>cqrsMapSplit</c>
/// config key; the CLI flag wins over config.
/// </summary>
public enum CqrsMapSplitMode
{
    Count,
    Feature,
    Unsplit,
}
