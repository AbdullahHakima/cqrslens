namespace CqrsLens;

/// <summary>Writes the post-scan console summary and evaluates <c>--strict</c>.</summary>
internal static class ScanReporter
{
    public static void Write(
        AnalysisResult result,
        CliOptions options,
        ReportOutputPaths outputs,
        ArchitectureDiff? diff,
        TextWriter stdout,
        TextWriter stderr)
    {
        var palette = ConsolePalette.Resolve();
        var glyphs = ConsoleGlyphs.Resolve(options.Ascii);
        var useColor = ConsoleArtwork.ShouldUseColor(stdout, options.Color);
        var cardShown = ConsoleArtwork.ShouldShowBanner(
            stdout,
            options.Banner,
            options.GitHubAnnotations);

        if (!cardShown)
            WriteScanTotals(result, stdout, useColor, palette);

        WritePath(stdout, "Report", outputs.Report, useColor, palette);

        if (outputs.Sarif is not null)
            WritePath(stdout, "SARIF", outputs.Sarif, useColor, palette);
        if (outputs.Summary is not null)
            WritePath(stdout, "Summary", outputs.Summary, useColor, palette);
        if (diff is not null)
            stdout.WriteLine(ArchitectureDiffer.FormatConsoleSummary(diff));
        if (outputs.Diff is not null)
            WritePath(stdout, "Diff", outputs.Diff, useColor, palette);

        if (result.SkippedFiles.Count > 0)
        {
            WriteWarning(
                stderr,
                $"{result.SkippedFiles.Count} source file(s) skipped - see the report.",
                useColor,
                palette);
        }

        var nonSkipDiagnostics = result.Diagnostics.Count(d =>
            d.Code != AnalysisResult.SourceFileSkippedCode);
        if (nonSkipDiagnostics > 0)
        {
            WriteWarning(
                stderr,
                $"{nonSkipDiagnostics} scan diagnostic(s) - see the report.",
                useColor,
                palette);
        }

        WriteCheckCounts(result, stdout, useColor, palette, glyphs);
        WriteBaselineStatus(result, options, stdout, useColor, palette);
    }

    public static int StrictExitCode(AnalysisResult result, CliOptions options, TextWriter stderr)
    {
        var newErrors = result.Checks.Count(c =>
            c.Severity == CheckSeverity.Error && (!result.BaselineApplied || !c.IsKnown));
        if (!options.Strict || newErrors == 0)
            return ExitCodes.Success;

        var useColor = ConsoleArtwork.ShouldUseColor(stderr, options.Color);
        var palette = ConsolePalette.Resolve();

        if (!result.CanRunStrictChecks)
        {
            WriteWarning(
                stderr,
                "--strict skipped because the scan is incomplete or ambiguous; " +
                "see the diagnostics in the report.",
                useColor,
                palette);
            return ExitCodes.Success;
        }

        var suffix = result.BaselineApplied
            ? "new architecture check error(s)"
            : "architecture check error(s)";
        WriteError(stderr, $"--strict - {newErrors} {suffix} found.", useColor, palette);
        return ExitCodes.StrictFailure;
    }

    private static void WriteScanTotals(
        AnalysisResult result,
        TextWriter stdout,
        bool useColor,
        ConsolePalette palette)
    {
        var totals =
            $"{result.Projects.Count} project(s), " +
            $"{result.Requests.Count} request(s), {result.Handlers.Count} handler(s), " +
            $"{result.Validators.Count} validator(s), {result.Behaviors.Count} behavior(s), " +
            $"{result.DbContexts.Count} DbContext(s).";

        if (!useColor)
        {
            stdout.WriteLine("Scanned " + totals);
            return;
        }

        stdout.WriteLine(
            AnsiColor.Colorize("Scanned", palette.InkMuted) + " " +
            AnsiColor.Colorize(totals, palette.InkDim));
    }

    private static void WritePath(
        TextWriter stdout,
        string label,
        string path,
        bool useColor,
        ConsolePalette palette)
    {
        if (!useColor)
        {
            stdout.WriteLine($"{label} written to {path}");
            return;
        }

        stdout.WriteLine(
            AnsiColor.Colorize(label + " written to", palette.InkMuted) + " " +
            AnsiColor.Colorize(path, palette.Ink));
    }

    private static void WriteCheckCounts(
        AnalysisResult result,
        TextWriter stdout,
        bool useColor,
        ConsolePalette palette,
        ConsoleGlyphs glyphs)
    {
        if (result.Checks.Count == 0)
            return;

        var errors = result.Checks.Count(c => c.Severity == CheckSeverity.Error);
        var warnings = result.Checks.Count - errors;

        if (!useColor)
        {
            stdout.WriteLine(
                $"Architecture checks: {errors} error(s), {warnings} warning(s) - see the report.");
            return;
        }

        var (icon, color) = errors > 0
            ? (glyphs.Fail, palette.Error)
            : warnings > 0
                ? (glyphs.Warn, palette.Warn)
                : (glyphs.Pass, palette.Pass);

        stdout.WriteLine(
            AnsiColor.Colorize($"{icon} Architecture checks", color, bold: true) + " " +
            AnsiColor.Colorize(
                $"{errors} error(s), {warnings} warning(s) - see the report.",
                palette.InkMuted));
    }

    private static void WriteBaselineStatus(
        AnalysisResult result,
        CliOptions options,
        TextWriter stdout,
        bool useColor,
        ConsolePalette palette)
    {
        if (!result.BaselineApplied)
            return;

        var known = result.Checks.Count(c => c.IsKnown);
        var newFindings = result.Checks.Count - known;

        if (options.WriteBaselinePath is not null)
        {
            if (!useColor)
            {
                stdout.WriteLine(
                    $"Baseline written to {options.WriteBaselinePath}: {known} finding(s) recorded.");
                return;
            }

            stdout.WriteLine(
                AnsiColor.Colorize("Baseline written to", palette.InkMuted) + " " +
                AnsiColor.Colorize(options.WriteBaselinePath, palette.Ink) + " " +
                AnsiColor.Colorize($"{known} finding(s) recorded.", palette.InkMuted));
            return;
        }

        if (!useColor)
        {
            stdout.WriteLine($"Baseline: {known} known finding(s), {newFindings} new finding(s).");
            return;
        }

        stdout.WriteLine(
            AnsiColor.Colorize("Baseline:", palette.InkMuted) + " " +
            AnsiColor.Colorize($"{known} known finding(s), {newFindings} new finding(s).", palette.InkDim));
    }

    private static void WriteWarning(
        TextWriter stderr,
        string message,
        bool useColor,
        ConsolePalette palette)
    {
        if (!useColor)
        {
            stderr.WriteLine($"warning: {message}");
            return;
        }

        stderr.WriteLine(
            AnsiColor.Colorize("warning:", palette.Warn, bold: true) + " " +
            AnsiColor.Colorize(message, palette.InkDim));
    }

    private static void WriteError(
        TextWriter stderr,
        string message,
        bool useColor,
        ConsolePalette palette)
    {
        if (!useColor)
        {
            stderr.WriteLine($"error: {message}");
            return;
        }

        stderr.WriteLine(
            AnsiColor.Colorize("error:", palette.Error, bold: true) + " " +
            AnsiColor.Colorize(message, palette.InkDim));
    }
}
