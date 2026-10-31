namespace CqrsLens;

/// <summary>Resolves and validates the report, SARIF, summary and diff destinations.</summary>
internal static class ReportOutputPathsResolver
{
    public static ReportOutputPaths Resolve(CliOptions options, ReportFormat format)
    {
        var output = options.Output ?? Path.Combine(
            Directory.GetCurrentDirectory(),
            ReportFormatResolver.DefaultFileName(format));
        var fullReport = Path.GetFullPath(output);
        if (IsDirectoryOutput(output, fullReport))
            throw new CliFailureException($"output path is a directory: {output}", ExitCodes.Usage);

        var fullSarif = ResolveOptional(options.SarifPath, "SARIF");
        var fullSummary = ResolveOptional(options.SummaryPath, "summary");
        var fullDiff = ResolveOptional(options.DiffOutputPath, "diff output");

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (fullSarif is not null && string.Equals(fullSarif, fullReport, comparison))
            throw new CliFailureException("--sarif and the report output must be different files.", ExitCodes.Usage);

        if (fullSummary is not null &&
            (string.Equals(fullSummary, fullReport, comparison) ||
             string.Equals(fullSummary, fullSarif, comparison)))
        {
            throw new CliFailureException(
                "--summary and the other output paths must be different files.",
                ExitCodes.Usage);
        }

        if (fullDiff is not null &&
            (string.Equals(fullDiff, fullReport, comparison) ||
             string.Equals(fullDiff, fullSarif, comparison) ||
             string.Equals(fullDiff, fullSummary, comparison)))
        {
            throw new CliFailureException(
                "--diff-output and the other output paths must be different files.",
                ExitCodes.Usage);
        }

        return new ReportOutputPaths(fullReport, fullSarif, fullSummary, fullDiff);
    }

    private static string? ResolveOptional(string? raw, string label)
    {
        if (raw is null)
            return null;

        var full = Path.GetFullPath(raw);
        if (IsDirectoryOutput(raw, full))
            throw new CliFailureException($"{label} path is a directory: {raw}", ExitCodes.Usage);

        return full;
    }

    private static bool IsDirectoryOutput(string raw, string fullPath)
        => Directory.Exists(fullPath) ||
           raw.EndsWith(Path.DirectorySeparatorChar) ||
           raw.EndsWith(Path.AltDirectorySeparatorChar);
}
