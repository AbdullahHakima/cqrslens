namespace CqrsLens;

internal sealed record SourceFile(string Path, string Code, string? ProjectName);

/// <summary>
/// One source file with the text scan and code mask produced exactly once for the
/// whole analysis. Shape discovery, deep-slice enrichment and Roslyn fallback all
/// read this same fragment instead of reparsing the file.
/// </summary>
internal sealed class ParsedSourceFile
{
    public ParsedSourceFile(SourceFile file, CSharpScanner.FileScan? scan, SourceMask? mask)
    {
        File = file;
        Scan = scan;
        Mask = mask;
    }

    public SourceFile File { get; }
    public CSharpScanner.FileScan? Scan { get; }
    public SourceMask? Mask { get; }
}

/// <summary>
/// The files read for one analysis plus their shared text-parsing artifacts.
/// Built once by <see cref="Analyzer"/> and passed to every scanner stage.
/// </summary>
internal sealed class SourceWorkspace
{
    public SourceWorkspace(
        IReadOnlyList<SourceFile> files,
        IReadOnlyList<ParsedSourceFile> parsedFiles)
    {
        Files = files;
        ParsedFiles = parsedFiles;
    }

    public IReadOnlyList<SourceFile> Files { get; }

    /// <summary>Parsed files in stable path order. A file whose text parse failed has a null Scan.</summary>
    public IReadOnlyList<ParsedSourceFile> ParsedFiles { get; }

    public IEnumerable<ParsedSourceFile> SuccessfullyParsed =>
        ParsedFiles.Where(f => f.Scan is not null && f.Mask is not null);

    /// <summary>
    /// Builds a workspace from in-memory sources. Used by tests and tools that already
    /// hold source text and want the same single-parse pipeline as the CLI.
    /// </summary>
    internal static SourceWorkspace FromFiles(IReadOnlyList<SourceFile> files)
    {
        var parsed = files
            .Select(file =>
            {
                var mask = SourceMask.Build(file.Code);
                return new ParsedSourceFile(file, CSharpScanner.ScanText(file.Code, mask), mask);
            })
            .ToList();
        return new SourceWorkspace(files, parsed);
    }
}

internal interface IArchitectureScanner
{
    void Scan(
        SourceWorkspace workspace,
        IReadOnlySet<string> markers,
        IReadOnlySet<string> handlerMarkers,
        AnalysisResult result);
}
