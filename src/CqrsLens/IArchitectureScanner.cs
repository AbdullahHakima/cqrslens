namespace CqrsLens;

internal sealed record SourceFile(string Path, string Code, string? ProjectName);

internal interface IArchitectureScanner
{
    void Scan(
        IReadOnlyList<SourceFile> files,
        IReadOnlySet<string> markers,
        AnalysisResult result);
}
