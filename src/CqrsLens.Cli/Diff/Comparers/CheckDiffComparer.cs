namespace CqrsLens;

/// <summary>
/// Diffs active check findings with multiset semantics so duplicate findings are
/// paired one-for-one instead of collapsing into a single new/resolved item.
/// </summary>
internal static class CheckDiffComparer
{
    public static CheckDiffSection Compare(
        IReadOnlyList<JsonCheckFinding> baseChecks,
        IReadOnlyList<JsonCheckFinding> headChecks)
    {
        static string Key(JsonCheckFinding finding)
            => BaselineKeys.Create(finding.Code, finding.Target, finding.Location?.File);

        var activeBase = baseChecks.Where(finding => !finding.Known).ToList();
        var activeHead = headChecks.Where(finding => !finding.Known).ToList();

        var baseRemaining = CountKeys(activeBase);
        var newFindings = new List<JsonCheckFinding>();
        foreach (var finding in activeHead)
        {
            var key = Key(finding);
            if (baseRemaining.TryGetValue(key, out var count) && count > 0)
                baseRemaining[key] = count - 1;
            else
                newFindings.Add(finding);
        }

        var headRemaining = CountKeys(activeHead);
        var resolvedFindings = new List<JsonCheckFinding>();
        foreach (var finding in activeBase)
        {
            var key = Key(finding);
            if (headRemaining.TryGetValue(key, out var count) && count > 0)
                headRemaining[key] = count - 1;
            else
                resolvedFindings.Add(finding);
        }

        return new CheckDiffSection
        {
            NewFindings = newFindings,
            ResolvedFindings = resolvedFindings,
        };
    }

    private static Dictionary<string, int> CountKeys(IEnumerable<JsonCheckFinding> findings)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var finding in findings)
        {
            var key = BaselineKeys.Create(finding.Code, finding.Target, finding.Location?.File);
            counts[key] = counts.TryGetValue(key, out var count) ? count + 1 : 1;
        }

        return counts;
    }
}
