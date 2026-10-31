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
        stdout.WriteLine(
            $"Scanned {result.Projects.Count} project(s): " +
            $"{result.Requests.Count} request(s), {result.Handlers.Count} handler(s), " +
            $"{result.Validators.Count} validator(s), {result.Behaviors.Count} behavior(s), " +
            $"{result.DbContexts.Count} DbContext(s).");
        stdout.WriteLine($"Report written to {outputs.Report}");

        if (outputs.Sarif is not null)
            stdout.WriteLine($"SARIF written to {outputs.Sarif}");
        if (outputs.Summary is not null)
            stdout.WriteLine($"Summary written to {outputs.Summary}");
        if (diff is not null)
            stdout.WriteLine(ArchitectureDiffer.FormatConsoleSummary(diff));
        if (outputs.Diff is not null)
            stdout.WriteLine($"Diff written to {outputs.Diff}");

        if (result.SkippedFiles.Count > 0)
            stderr.WriteLine($"warning: {result.SkippedFiles.Count} source file(s) skipped - see the report.");

        var nonSkipDiagnostics = result.Diagnostics.Count(d =>
            d.Code != AnalysisResult.SourceFileSkippedCode);
        if (nonSkipDiagnostics > 0)
            stderr.WriteLine($"warning: {nonSkipDiagnostics} scan diagnostic(s) - see the report.");

        WriteCheckCounts(result, stdout);
        WriteBaselineStatus(result, options, stdout);
    }

    public static int StrictExitCode(AnalysisResult result, CliOptions options, TextWriter stderr)
    {
        var newErrors = result.Checks.Count(c =>
            c.Severity == CheckSeverity.Error && (!result.BaselineApplied || !c.IsKnown));
        if (!options.Strict || newErrors == 0)
            return ExitCodes.Success;

        if (!result.CanRunStrictChecks)
        {
            stderr.WriteLine(
                "warning: --strict skipped because the scan is incomplete or ambiguous; " +
                "see the diagnostics in the report.");
            return ExitCodes.Success;
        }

        var suffix = result.BaselineApplied
            ? "new architecture check error(s)"
            : "architecture check error(s)";
        stderr.WriteLine($"error: --strict - {newErrors} {suffix} found.");
        return ExitCodes.StrictFailure;
    }

    private static void WriteCheckCounts(AnalysisResult result, TextWriter stdout)
    {
        if (result.Checks.Count == 0)
            return;

        var errors = result.Checks.Count(c => c.Severity == CheckSeverity.Error);
        var warnings = result.Checks.Count - errors;
        stdout.WriteLine($"Architecture checks: {errors} error(s), {warnings} warning(s) - see the report.");
    }

    private static void WriteBaselineStatus(AnalysisResult result, CliOptions options, TextWriter stdout)
    {
        if (!result.BaselineApplied)
            return;

        var known = result.Checks.Count(c => c.IsKnown);
        var newFindings = result.Checks.Count - known;
        if (options.WriteBaselinePath is not null)
        {
            stdout.WriteLine(
                $"Baseline written to {options.WriteBaselinePath}: {known} finding(s) recorded.");
            return;
        }

        stdout.WriteLine($"Baseline: {known} known finding(s), {newFindings} new finding(s).");
    }
}
