using System.Text;

namespace CqrsLens;

/// <summary>Renders the <c>--help</c> text and option reference.</summary>
internal static class UsageWriter
{
    private const int FlagWidth = 24;
    private const int Indent = 2;

    public static void Write(TextWriter writer, CliOptions? options = null)
    {
        var useColor = ConsoleArtwork.ShouldUseColor(writer, options?.Color);
        var palette = ConsolePalette.Resolve();
        var glyphs = ConsoleGlyphs.Resolve(options?.Ascii);
        var width = ConsoleWidth.Resolve();

        if (ConsoleArtwork.ShouldShowBanner(writer, options?.Banner, false))
            writer.WriteLine(ConsoleArtwork.RenderLogoBanner(ToolInfo.Version, useColor, options?.Ascii));

        WriteLine(writer, $"NOCTUA {ToolInfo.Version} - see your MediatR codebase.", useColor, palette.Ink, bold: true);
        writer.WriteLine();
        WriteLine(writer, "Usage: cqrslens <path> [options]", useColor, palette.Ink, bold: false);
        writer.WriteLine();

        foreach (var (heading, entries) in Sections())
        {
            WriteLine(writer, heading, useColor, palette.Accent, bold: true);
            foreach (var (flag, description) in entries)
                WriteEntry(writer, flag, description, width, useColor, palette);

            writer.WriteLine();
        }

        WriteLine(writer, "Exit codes", useColor, palette.Accent, bold: true);
        foreach (var line in new[]
                 {
                     $"0 ok  {glyphs.Separator}  1 usage error  {glyphs.Separator}  2 path not found",
                     $"3 no projects found  {glyphs.Separator}  4 strict check failure  {glyphs.Separator}  5 unexpected failure",
                 })
        {
            writer.Write(new string(' ', Indent));
            WriteLine(writer, line, useColor, palette.InkMuted, bold: false);
        }
    }

    private static (string Heading, (string Flag, string Description)[] Entries)[] Sections()
        => new[]
        {
            ("Report", new[]
            {
                ("-o, --output FILE",
                    "Report path. Default: ARCHITECTURE.md (.html or .json) in the current directory."),
                ("--format FORMAT",
                    "md (default), html, json, or agent. A matching -o extension implies the format."),
                ("--render MODE",
                    "HTML diagrams: mermaid (default) or svg (zero-JS inline SVG)."),
                ("--export-svg DIR",
                    "Also write every diagram as a standalone .svg file into DIR (created when missing)."),
                ("--summary FILE",
                    "Also write a compact Markdown job summary."),
                ("--generated-at ISO8601",
                    "Fixed timestamp for reproducible reports; timing is reported as 0 ms."),
            }),
            ("Analysis", new[]
            {
                ("--analysis MODE",
                    "text (default) or roslyn (opt-in)."),
                ("--slice FILTER",
                    "Trace vertical slices matching FILTER (endpoint, request, handler, entity, or event). " +
                    "Streams to stdout when -o is omitted."),
                ("--markers LIST",
                    "Extra request roots, e.g. ICommand,IQuery,IMessage. Requests behind interfaces " +
                    "deriving from IRequest are always followed."),
                ("--handler-markers LIST",
                    "Extra handler roots for external wrappers, e.g. ICommandHandler,IQueryHandler."),
                ("--cqrs-map-split MODE",
                    "count (default), feature, or unsplit. Overrides the cqrsMapSplit config key."),
                ("--config FILE",
                    "Config file. Default: <path>/.cqrslens.json when present. Holds analysis, render, " +
                    "watermark, markers, layers, and rules."),
            }),
            ("Checks and CI", new[]
            {
                ("--strict",
                    "Exit 4 when architecture checks report an error and the scan is complete."),
                ("--baseline FILE",
                    "Compare checks with a baseline; --strict then fails only on new errors."),
                ("--write-baseline FILE",
                    "Record current checks as the baseline, then continue."),
                ("--sarif FILE",
                    "Also write SARIF 2.1.0 for code scanning."),
                ("--diff PATH_OR_JSON",
                    "Compare against a base ARCHITECTURE.json or solution folder."),
                ("--diff-output FILE",
                    "Write the architecture diff report (.md for PR comments, or .json)."),
                ("--github-annotations",
                    "Emit file/line GitHub Actions annotations."),
                ("--ci-root DIR",
                    "Repository root used to rebase annotation and SARIF paths."),
            }),
            ("Console", new[]
            {
                ("--banner / --no-banner",
                    "Force or suppress the logo banner and summary card."),
                ("--color / --no-color",
                    "Force or disable ANSI TrueColor output (also respects NO_COLOR)."),
                ("--ascii / --unicode",
                    "Use single-width ASCII chrome. For CJK terminals, which draw box-drawing and " +
                    "block characters two cells wide. Also honours CQRSLENS_ASCII."),
                ("--watermark TEXT",
                    "Custom watermark rendered as block art in the console and as a stamp in reports."),
                ("--profile",
                    "Print per-stage timing and allocation totals to stderr."),
            }),
            ("Other", new[]
            {
                ("--mcp",
                    "Start the built-in Model Context Protocol server over JSON-RPC 2.0 stdio."),
                ("-h, --help",
                    "Show this help."),
                ("--version",
                    "Print the tool version and exit."),
            }),
        };

    private static void WriteEntry(
        TextWriter writer,
        string flag,
        string description,
        int width,
        bool useColor,
        ConsolePalette palette)
    {
        var descriptionColumn = Indent + FlagWidth + 2;
        var available = Math.Max(24, width - descriptionColumn);
        var lines = Wrap(description, available);

        var head = new string(' ', Indent) + flag.PadRight(FlagWidth) + "  ";
        Write(writer, head, useColor, palette.Ink, bold: true);
        WriteLine(writer, lines[0], useColor, palette.InkMuted, bold: false);

        for (var i = 1; i < lines.Count; i++)
        {
            writer.Write(new string(' ', descriptionColumn));
            WriteLine(writer, lines[i], useColor, palette.InkMuted, bold: false);
        }
    }

    private static List<string> Wrap(string text, int width)
    {
        var lines = new List<string>();
        var current = new StringBuilder();

        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current.Length == 0)
            {
                current.Append(word);
                continue;
            }

            if (current.Length + 1 + word.Length <= width)
            {
                current.Append(' ').Append(word);
                continue;
            }

            lines.Add(current.ToString());
            current.Clear();
            current.Append(word);
        }

        if (current.Length > 0)
            lines.Add(current.ToString());

        if (lines.Count == 0)
            lines.Add(string.Empty);

        return lines;
    }

    private static void WriteLine(
        TextWriter writer,
        string text,
        bool useColor,
        (byte R, byte G, byte B) color,
        bool bold)
    {
        writer.Write(useColor ? AnsiColor.Colorize(text, color, bold) : text);
        writer.WriteLine();
    }

    private static void Write(
        TextWriter writer,
        string text,
        bool useColor,
        (byte R, byte G, byte B) color,
        bool bold)
        => writer.Write(useColor ? AnsiColor.Colorize(text, color, bold) : text);
}
