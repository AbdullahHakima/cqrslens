using System.Text;

namespace CqrsLens;

/// <summary>Writes the architecture checks section, including baseline handling.</summary>
internal static class MarkdownChecksWriter
{
    internal static void Write(StringBuilder md, AnalysisResult result)
    {
        md.AppendLine("## Architecture Checks");
        md.AppendLine();
        AppendChecksMarkdown(md, result.Checks, result.BaselineApplied);
        md.AppendLine();
    }

    internal static void AppendChecksMarkdown(
        StringBuilder md,
        IReadOnlyList<CheckFinding> checks,
        bool baselineApplied = false)
    {
        if (checks.Count == 0)
        {
            md.AppendLine("All checks passed.");
            return;
        }

        var newChecks = baselineApplied
            ? checks.Where(c => !c.IsKnown).ToList()
            : checks.ToList();
        var knownChecks = baselineApplied
            ? checks.Where(c => c.IsKnown).ToList()
            : new List<CheckFinding>();

        var newErrors = newChecks.Count(c => c.Severity == CheckSeverity.Error);
        var newWarnings = newChecks.Count - newErrors;
        if (baselineApplied)
        {
            md.AppendLine(
                $"{newErrors} new error(s), {newWarnings} new warning(s); " +
                $"{knownChecks.Count} known finding(s) from the baseline:");
        }
        else
        {
            var errors = newChecks.Count(c => c.Severity == CheckSeverity.Error);
            var warnings = newChecks.Count - errors;
            md.AppendLine($"{errors} error(s), {warnings} warning(s):");
        }

        md.AppendLine();
        if (baselineApplied && newChecks.Count == 0)
        {
            md.AppendLine("No new findings.");
            md.AppendLine();
        }

        foreach (var check in newChecks)
            AppendCheckMarkdown(md, check, known: false);

        if (knownChecks.Count > 0)
        {
            md.AppendLine();
            md.AppendLine("Known (from baseline):");
            md.AppendLine();
            foreach (var check in knownChecks)
                AppendCheckMarkdown(md, check, known: true);
        }
    }

    private static void AppendCheckMarkdown(StringBuilder md, CheckFinding check, bool known)
    {
        var badge = known
            ? "ℹ️ Known"
            : check.Severity == CheckSeverity.Error ? "❌ Error" : "⚠️ Warning";
        var locality = check.Location is null
            ? ""
            : $" {MarkdownEscaping.CodeSpan($"{check.Location.File}:{check.Location.Line}:{check.Location.Column}")}";
        md.AppendLine(
            $"- {badge} **{MarkdownEscaping.InlineText(check.Code)}** — {MarkdownEscaping.InlineText(check.Message)}{locality}");
    }
}
