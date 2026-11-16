namespace CqrsLens;

/// <summary>
/// Builds the deterministic arc42-shaped Markdown documentation tree (<c>01-context.md</c>
/// through <c>06-findings.md</c>) as a pure view over <see cref="AnalysisResult"/> (F3 R3 / R-DOCS).
/// </summary>
public static class DocsTreeBuilder
{
    public static IReadOnlyList<(string FileName, string Content)> BuildPages(
        AnalysisResult result,
        CqrsMapSplitMode cqrsMapSplit = CqrsMapSplitMode.Count)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new (string FileName, string Content)[]
        {
            ("01-context.md", DocsContextPageWriter.Write(result)),
            ("02-building-blocks.md", DocsBuildingBlocksPageWriter.Write(result, cqrsMapSplit)),
            ("03-runtime.md", DocsRuntimePageWriter.Write(result)),
            ("04-data-model.md", DocsDataModelPageWriter.Write(result)),
            ("05-cross-cutting.md", DocsCrossCuttingPageWriter.Write(result)),
            ("06-findings.md", DocsFindingsPageWriter.Write(result)),
        };
    }
}
