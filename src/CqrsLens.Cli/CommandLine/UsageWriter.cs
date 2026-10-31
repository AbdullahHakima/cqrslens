namespace CqrsLens;

/// <summary>Renders the <c>--help</c> text and option reference.</summary>
internal static class UsageWriter
{
    public static void Write(TextWriter writer)
    {
        if (ConsoleArtwork.ShouldShowBanner(writer, null, false))
        {
            var useColor = ConsoleArtwork.ShouldUseColor(writer, null);
            writer.WriteLine(ConsoleArtwork.RenderLogoBanner(ToolInfo.Version, useColor));
        }

        writer.WriteLine($"CqrsLens {ToolInfo.Version} - see your MediatR codebase.");
        writer.WriteLine();
        writer.WriteLine("Usage: cqrslens <path> [-o OUTPUT] [--format md|html|json|agent] [--slice FILTER] [--render mermaid|svg] [--config FILE] [--analysis text|roslyn] [--generated-at ISO8601] [--markers ICommand,IQuery] [--handler-markers ICommandHandler,IQueryHandler] [--watermark TEXT] [--banner|--no-banner] [--color|--no-color] [--baseline FILE] [--write-baseline FILE] [--diff PATH_OR_JSON] [--diff-output FILE] [--sarif FILE] [--summary FILE] [--github-annotations] [--ci-root DIR] [--strict]");
        writer.WriteLine();
        writer.WriteLine("  <path>        Folder of the .NET solution to scan (searched recursively).");
        writer.WriteLine("  -o, --output  Report path. Default: ARCHITECTURE.md (.html or .json) in the current directory.");
        writer.WriteLine("  --config      Config file. Default: <path>/.cqrslens.json when present.");
        writer.WriteLine("                Settings: analysis, render, watermark, markers, handlerMarkers, include/exclude globs, layers, rules.");
        writer.WriteLine("  --format      Report format: md (default), html, json, or agent. A .html, .json, or .agent.md -o path implies that format.");
        writer.WriteLine("  --slice       Filter and trace vertical slices matching FILTER (endpoint, request, handler, entity, or event).");
        writer.WriteLine("                Streams directly to stdout when -o is omitted.");
        writer.WriteLine("  --mcp         Start the built-in Model Context Protocol (MCP) server over JSON-RPC 2.0 stdio.");
        writer.WriteLine("  --profile     Print per-stage timing and allocation totals to stderr.");
        writer.WriteLine("  --render      HTML diagram rendering: mermaid (default) or svg (zero-JS inline SVG).");
        writer.WriteLine("  --analysis    Analysis backend: text (default) or roslyn (opt-in).");
        writer.WriteLine("  --generated-at  Fixed ISO-8601 timestamp for reproducible reports (tests/CI);");
        writer.WriteLine("                timing is reported as 0 ms in this mode.");
        writer.WriteLine("  --markers     Comma-separated custom marker interface names that also count");
        writer.WriteLine("                as request roots (e.g. ICommand,IQuery,IMessage). Requests hidden");
        writer.WriteLine("                behind interfaces deriving from IRequest are always followed.");
        writer.WriteLine("  --handler-markers  Comma-separated custom handler roots for external wrappers");
        writer.WriteLine("                (e.g. ICommandHandler,IQueryHandler). Source-declared wrappers");
        writer.WriteLine("                are followed automatically.");
        writer.WriteLine("  --watermark   Custom watermark text rendered as block ASCII art in the console");
        writer.WriteLine("                and embedded as a watermark stamp in Markdown, HTML, and JSON reports.");
        writer.WriteLine("  --banner      Force rendering the ASCII art logo banner and summary card.");
        writer.WriteLine("  --no-banner   Suppress the ASCII art logo banner and summary card.");
        writer.WriteLine("  --color       Force ANSI 24-bit TrueColor gradients in console output.");
        writer.WriteLine("  --no-color    Disable ANSI colors in console output (also respects NO_COLOR).");
        writer.WriteLine("  --baseline    Compare checks with a baseline file; --strict then fails only");
        writer.WriteLine("                on errors not present in the baseline.");
        writer.WriteLine("  --write-baseline  Record current checks as the baseline, then continue.");
        writer.WriteLine("  --diff        Compare the current scan against a base ARCHITECTURE.json file or");
        writer.WriteLine("                base solution folder to see what changed in the CQRS surface.");
        writer.WriteLine("  --diff-output Write the architecture diff report (.md for PR comments, or .json).");
        writer.WriteLine("  --sarif       Also write SARIF 2.1.0 for code scanning.");
        writer.WriteLine("  --summary     Also write a compact Markdown job summary.");
        writer.WriteLine("  --github-annotations  Emit file/line GitHub Actions annotations.");
        writer.WriteLine("  --ci-root     Repository root used to rebase annotation/SARIF paths.");
        writer.WriteLine("  --strict      Exit with code 4 when architecture checks report an error");
        writer.WriteLine("                and the scan is complete; incomplete scans report diagnostics.");
        writer.WriteLine("  -h, --help    Show this help.");
        writer.WriteLine("  --version     Print the tool version and exit.");
        writer.WriteLine();
        writer.WriteLine("Exit codes: 0 ok - 1 usage error - 2 path not found - 3 no projects found - 4 strict check failure - 5 unexpected failure.");
    }
}
