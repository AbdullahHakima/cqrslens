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

        foreach (var group in GroupChecks(newChecks))
            AppendCheckGroupMarkdown(md, group, known: false);

        if (knownChecks.Count > 0)
        {
            md.AppendLine();
            md.AppendLine("Known (from baseline):");
            md.AppendLine();
            foreach (var group in GroupChecks(knownChecks))
                AppendCheckGroupMarkdown(md, group, known: true);
        }
    }

    internal sealed record CheckFindingGroup(
        string Code,
        CheckSeverity HighestSeverity,
        int TotalCount,
        IReadOnlyList<(string Note, int RepeatCount)> SharedNotes,
        IReadOnlyList<(CheckFinding Finding, string DisplayMessage, int RepeatCount)> Items);

    internal static IReadOnlyList<CheckFindingGroup> GroupChecks(IReadOnlyList<CheckFinding> checks)
    {
        if (checks.Count == 0)
            return Array.Empty<CheckFindingGroup>();

        var groups = new List<CheckFindingGroup>();
        var orderedGroups = checks
            .GroupBy(c => c.Code, StringComparer.Ordinal)
            .OrderBy(g => g.Min(c => SeverityRank(c.Severity)))
            .ThenByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal);

        foreach (var group in orderedGroups)
        {
            var list = group.ToList();
            var highestSeverity = list
                .OrderBy(c => SeverityRank(c.Severity))
                .First().Severity;

            var trailingCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            var trailingOrder = new List<string>();
            foreach (var c in list)
            {
                if (TrySplitTrailingSentence(c.Message, out _, out var trailing))
                {
                    if (!trailingCounts.TryGetValue(trailing, out var count))
                    {
                        trailingOrder.Add(trailing);
                        trailingCounts[trailing] = 1;
                    }
                    else
                    {
                        trailingCounts[trailing] = count + 1;
                    }
                }
            }

            var sharedTrailing = new HashSet<string>(
                trailingOrder.Where(t => trailingCounts[t] >= 2),
                StringComparer.Ordinal);
            var sharedNotes = trailingOrder
                .Where(sharedTrailing.Contains)
                .Select(t => (Note: t, RepeatCount: trailingCounts[t]))
                .ToList();

            var trimmed = list
                .Select(c =>
                {
                    if (TrySplitTrailingSentence(c.Message, out var prefix, out var trailing) &&
                        sharedTrailing.Contains(trailing))
                    {
                        return (Finding: c, DisplayMessage: prefix);
                    }

                    return (Finding: c, DisplayMessage: c.Message);
                })
                .ToList();

            var items = trimmed
                .GroupBy(
                    x => (
                        x.Finding.Severity,
                        x.Finding.Code,
                        x.DisplayMessage,
                        LocationKey: x.Finding.Location is null
                            ? ""
                            : $"{x.Finding.Location.File}:{x.Finding.Location.Line}:{x.Finding.Location.Column}"),
                    x => x)
                .Select(g =>
                {
                    var first = g.First();
                    return (first.Finding, first.DisplayMessage, RepeatCount: g.Count());
                })
                .ToList();

            groups.Add(new CheckFindingGroup(
                group.Key,
                highestSeverity,
                list.Count,
                sharedNotes,
                items));
        }

        return groups;
    }

    private static bool TrySplitTrailingSentence(string message, out string prefix, out string trailing)
    {
        var idx = message.LastIndexOf(". ", StringComparison.Ordinal);
        if (idx > 0 && idx + 2 < message.Length)
        {
            prefix = message[..(idx + 1)];
            trailing = message[(idx + 2)..];
            return true;
        }

        prefix = message;
        trailing = "";
        return false;
    }

    private static int SeverityRank(CheckSeverity severity) => severity switch
    {
        CheckSeverity.Error => 0,
        CheckSeverity.Warning => 1,
        _ => 2,
    };

    private static void AppendCheckGroupMarkdown(StringBuilder md, CheckFindingGroup group, bool known)
    {
        var headerIcon = known
            ? "ℹ️"
            : group.HighestSeverity == CheckSeverity.Error ? "❌" : "⚠️";
        md.AppendLine($"### {headerIcon} {MarkdownEscaping.InlineText(group.Code)} ({group.TotalCount})");
        md.AppendLine();
        foreach (var (note, count) in group.SharedNotes)
        {
            md.AppendLine($"- ℹ️ _{MarkdownEscaping.InlineText(note)} (×{count})_");
        }
        foreach (var (check, displayMessage, repeatCount) in group.Items)
        {
            AppendCheckMarkdown(md, check, displayMessage, repeatCount, known);
        }
        md.AppendLine();
    }

    private static void AppendCheckMarkdown(
        StringBuilder md,
        CheckFinding check,
        string displayMessage,
        int repeatCount,
        bool known)
    {
        var badge = known
            ? "ℹ️ Known"
            : check.Severity == CheckSeverity.Error ? "❌ Error" : "⚠️ Warning";
        var repeat = repeatCount > 1 ? $" (×{repeatCount})" : "";
        var locality = check.Location is null
            ? ""
            : $" {MarkdownEscaping.CodeSpan($"{check.Location.File}:{check.Location.Line}:{check.Location.Column}")}";
        md.AppendLine(
            $"- {badge} **{MarkdownEscaping.InlineText(check.Code)}** — {MarkdownEscaping.InlineText(displayMessage)}{repeat}{locality}");
    }
}
