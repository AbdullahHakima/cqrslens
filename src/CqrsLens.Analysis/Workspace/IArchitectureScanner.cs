namespace CqrsLens;

internal interface IArchitectureScanner
{
    void Scan(
        SourceWorkspace workspace,
        IReadOnlySet<string> markers,
        IReadOnlySet<string> handlerMarkers,
        AnalysisResult result);
}
