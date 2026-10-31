namespace CqrsLens;

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
