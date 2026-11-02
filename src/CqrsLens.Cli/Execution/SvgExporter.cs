using System.Text;

namespace CqrsLens;

/// <summary>
/// Writes every diagram the report contains as a standalone SVG file. Reuses the
/// same builders and <see cref="MermaidSvgRenderer"/> as the HTML report; it does
/// not build a parallel diagram pipeline and does not change what the diagrams hold.
/// Export is additive and never changes the report, the exit code or --strict.
/// </summary>
internal static class SvgExporter
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
                $"--export-svg path is a file, not a directory: {directory}",
                ExitCodes.Usage);
        }

        var diagrams = BuildDiagrams(result, cqrsMapSplit);

        try
        {
            Directory.CreateDirectory(fullDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            throw new CliFailureException(
                $"could not create --export-svg directory '{directory}': {ex.Message}",
                ExitCodes.Unexpected);
        }

        var written = new List<string>(diagrams.Count);
        var failed = new List<string>();
        foreach (var (name, source) in diagrams)
        {
            var path = Path.Combine(fullDirectory, name);
            try
            {
                File.WriteAllText(path, ToStandaloneSvg(MermaidSvgRenderer.Render(source)), new UTF8Encoding(false));
                written.Add(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
            {
                failed.Add($"{name} ({ex.Message})");
            }
        }

        if (failed.Count > 0)
        {
            throw new CliFailureException(
                $"could not write {failed.Count} diagram(s) to --export-svg directory '{directory}': " +
                string.Join("; ", failed),
                ExitCodes.Unexpected);
        }

        return written;
    }

    /// <summary>
    /// The ordered diagram set the SVG-rendered HTML report produces for the same
    /// scan, with deterministic file names. Diagrams that exceed the report's Mermaid
    /// safety limit are omitted exactly as the report omits them.
    /// </summary>
    internal static IReadOnlyList<(string Name, string Source)> BuildDiagrams(AnalysisResult result)
        => BuildDiagrams(result, CqrsMapSplitMode.Count);

    internal static IReadOnlyList<(string Name, string Source)> BuildDiagrams(
        AnalysisResult result,
        CqrsMapSplitMode cqrsMapSplit)
    {
        var entries = new List<(string Name, string Source)>();

        AddSingle(entries, "project-graph.svg", DiagramBuilder.ProjectGraph(result));
        AddSingle(entries, "layers.svg", DiagramBuilder.LayerGraph(result));
        AddFamily(entries, "cqrs-map", DiagramBuilder.CqrsMapSections(result, cqrsMapSplit).Select(s => s.Source));
        AddFamily(entries, "flow", DiagramBuilder.FlowDiagramSections(result).Select(s => s.Source));
        AddSingle(entries, "notifications.svg", DiagramBuilder.NotificationMap(result));
        AddSingle(entries, "behavior-chain.svg", DiagramBuilder.BehaviorChain(result));
        AddFamily(
            entries,
            "er-diagram",
            result.DbContexts
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Namespace, StringComparer.Ordinal)
                .Select(DiagramBuilder.ErDiagram)
                .Where(d => d is not null)!);

        return entries;
    }

    private static void AddSingle(List<(string Name, string Source)> entries, string name, string? source)
    {
        if (source is not null && DiagramBuilder.IsWithinMermaidLimits(source))
            entries.Add((name, source));
    }

    private static void AddFamily(
        List<(string Name, string Source)> entries,
        string baseName,
        IEnumerable<string> sources)
    {
        var index = 0;
        foreach (var source in sources)
        {
            if (!DiagramBuilder.IsWithinMermaidLimits(source))
                continue;

            index++;
            var name = index == 1 ? $"{baseName}.svg" : $"{baseName}-{index}.svg";
            entries.Add((name, source));
        }
    }

    /// <summary>
    /// Turns the renderer's embedded <c>&lt;div class="svg-diagram"&gt;&lt;svg&gt;…</c>
    /// markup into a complete standalone SVG document (root <c>&lt;svg&gt;</c> with
    /// its own namespace, viewBox and inline presentation).
    /// </summary>
    internal static string ToStandaloneSvg(string rendered)
    {
        var start = rendered.IndexOf("<svg", StringComparison.Ordinal);
        var end = rendered.LastIndexOf("</svg>", StringComparison.Ordinal);
        if (start < 0 || end < 0)
            throw new InvalidOperationException("diagram renderer did not produce an <svg> element.");

        var svg = rendered[start..(end + "</svg>".Length)];
        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" + svg + "\n";
    }
}
