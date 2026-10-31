namespace CqrsLens;

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
