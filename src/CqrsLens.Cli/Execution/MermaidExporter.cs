using System.Text;

namespace CqrsLens;

/// <summary>
/// Writes every diagram the report contains as a standalone Mermaid (.mmd) file:
/// the diagram sources exactly as the reports embed them, one file per diagram,
/// under the same names as the SVG export. Reuses
/// <see cref="SvgExporter.BuildDiagrams"/> so both exports always hold the same
/// diagram set; export is additive and never changes the report, the exit code
/// or --strict.
/// </summary>
internal static class MermaidExporter
{
    /// <summary>
    /// Exports the report's diagrams into <paramref name="directory"/> (created when
    /// missing) and returns the written paths. Throws <see cref="CliFailureException"/>
    /// with a non-zero exit code when the directory or any file cannot be written.
    /// </summary>
    internal static IReadOnlyList<string> Export(AnalysisResult result, string directory)
        => Export(result, directory, CqrsMapSplitMode.Count);

    internal static IReadOnlyList<string> Export(
        AnalysisResult result,
        string directory,
        CqrsMapSplitMode cqrsMapSplit)
    {
        var fullDirectory = Path.GetFullPath(directory);
        if (File.Exists(fullDirectory))
        {
            throw new CliFailureException(
                $"--export-mermaid path is a file, not a directory: {directory}",
                ExitCodes.Usage);
        }

        var diagrams = SvgExporter.BuildDiagrams(result, cqrsMapSplit);

        try
        {
            Directory.CreateDirectory(fullDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            throw new CliFailureException(
                $"could not create --export-mermaid directory '{directory}': {ex.Message}",
                ExitCodes.Unexpected);
        }

        var written = new List<string>(diagrams.Count);
        var failed = new List<string>();
        foreach (var (name, source) in diagrams)
        {
            var fileName = Path.ChangeExtension(name, ".mmd");
            var path = Path.Combine(fullDirectory, fileName);
            try
            {
                File.WriteAllText(
                    path,
                    source.EndsWith("\n", StringComparison.Ordinal) ? source : source + "\n",
                    new UTF8Encoding(false));
                written.Add(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
            {
                failed.Add($"{fileName} ({ex.Message})");
            }
        }

        if (failed.Count > 0)
        {
            throw new CliFailureException(
                $"could not write {failed.Count} diagram(s) to --export-mermaid directory '{directory}': " +
                string.Join("; ", failed),
                ExitCodes.Unexpected);
        }

        return written;
    }
}
