namespace CqrsLens;

/// <summary>Applies a baseline document to the findings of one analysis run.</summary>
public static class BaselineMatcher
{
    /// <summary>Marks the current checks as known after generating a new baseline.</summary>
    public static void MarkAllKnown(AnalysisResult result, string path)
    {
        for (var i = 0; i < result.Checks.Count; i++)
            result.Checks[i] = result.Checks[i] with { IsKnown = true };

        result.BaselineApplied = true;
        result.BaselinePath = path;
    }

    public static void Apply(AnalysisResult result, BaselineDocument document, string path)
    {
        var remaining = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var entry in document.Findings)
        {
            var key = BaselineKeys.Create(entry.Code, entry.Target, entry.File);
            remaining[key] = remaining.TryGetValue(key, out var count) ? count + 1 : 1;
        }

        for (var i = 0; i < result.Checks.Count; i++)
        {
            var finding = result.Checks[i];
            if (remaining.TryGetValue(finding.BaselineKey, out var count) && count > 0)
            {
                remaining[finding.BaselineKey] = count - 1;
                result.Checks[i] = finding with { IsKnown = true };
            }
        }

        result.BaselineApplied = true;
        result.BaselinePath = path;
    }
}
