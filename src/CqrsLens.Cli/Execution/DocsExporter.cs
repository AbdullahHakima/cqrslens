using System.Text;

namespace CqrsLens;

/// <summary>
/// Exports the arc42 documentation tree (<c>01-context.md</c>..<c>06-findings.md</c>)
/// plus standalone SVG diagrams into <c>diagrams/</c> (F3 R-DOCS / R-DIAGRAMS).
/// </summary>
internal static class DocsExporter
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    internal static IReadOnlyList<string> Export(
        AnalysisResult result,
        string directory,
        CqrsMapSplitMode cqrsMapSplit = CqrsMapSplitMode.Count)
    {
        var fullDirectory = Path.GetFullPath(directory);
        if (File.Exists(fullDirectory))
        {
            throw new CliFailureException(
                $"--export-docs path is a file, not a directory: {directory}",
                ExitCodes.Usage);
        }

        var diagramsDir = Path.Combine(fullDirectory, "diagrams");
        if (File.Exists(diagramsDir))
        {
            throw new CliFailureException(
                $"--export-docs diagrams path is a file, not a directory: {diagramsDir}",
                ExitCodes.Usage);
        }

        var pages = DocsTreeBuilder.BuildPages(result, cqrsMapSplit);

        try
        {
            Directory.CreateDirectory(fullDirectory);
            if (Directory.Exists(diagramsDir))
            {
                foreach (var existingSvg in Directory.EnumerateFiles(diagramsDir, "*.svg", SearchOption.TopDirectoryOnly))
                    File.Delete(existingSvg);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            throw new CliFailureException(
                $"could not prepare --export-docs directory '{directory}': {ex.Message}",
                ExitCodes.Unexpected);
        }

        var written = new List<string>(pages.Count + 8);
        foreach (var (fileName, content) in pages)
        {
            var path = Path.Combine(fullDirectory, fileName);
            try
            {
                File.WriteAllText(path, content, Utf8NoBom);
                written.Add(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
            {
                throw new CliFailureException(
                    $"could not write '{fileName}' in --export-docs directory '{directory}': {ex.Message}",
                    ExitCodes.Unexpected);
            }
        }

        var svgPaths = SvgExporter.Export(result, diagramsDir, cqrsMapSplit);
        written.AddRange(svgPaths);

        return written;
    }
}
