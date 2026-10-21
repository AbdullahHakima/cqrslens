namespace CqrsLens;

/// <summary>
/// Writes GitHub Actions workflow-command annotations for architecture checks.
/// Known baseline findings are intentionally omitted so a PR only shows what it
/// introduced. Paths can be rebased from the scan root to the repository root
/// with --ci-root.
/// </summary>
public static class GitHubAnnotations
{
    public static void Write(AnalysisResult result, TextWriter writer, string? ciRoot = null)
    {
        foreach (var check in result.Checks)
        {
            if (result.BaselineApplied && check.IsKnown)
                continue;

            var command = check.Severity == CheckSeverity.Error ? "error" : "warning";
            var properties = new List<string>();
            if (check.Location is not null)
            {
                properties.Add($"file={EscapeProperty(CiPath.Relative(result, check.Location.File, ciRoot))}");
                properties.Add($"line={check.Location.Line}");
                properties.Add($"col={check.Location.Column}");
            }

            properties.Add($"title={EscapeProperty(check.Code)}");
            writer.WriteLine(
                $"::{command} {string.Join(",", properties)}::{EscapeMessage(check.Message)}");
        }
    }

    private static string EscapeProperty(string value)
        => EscapeMessage(value)
            .Replace(":", "%3A", StringComparison.Ordinal)
            .Replace(",", "%2C", StringComparison.Ordinal);

    private static string EscapeMessage(string value)
        => value
            .Replace("%", "%25", StringComparison.Ordinal)
            .Replace("\r", "%0D", StringComparison.Ordinal)
            .Replace("\n", "%0A", StringComparison.Ordinal);
}
