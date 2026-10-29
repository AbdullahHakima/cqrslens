namespace CqrsLens;

/// <summary>Applies <c>--baseline</c> or <c>--write-baseline</c> to a scan result.</summary>
internal static class BaselineProcessor
{
    public static void Apply(CliOptions options, AnalysisResult result)
    {
        if (options.WriteBaselinePath is not null)
        {
            BaselineStore.Write(result.Checks, options.WriteBaselinePath);
            BaselineMatcher.MarkAllKnown(result, options.WriteBaselinePath);
            return;
        }

        if (options.BaselinePath is not null)
        {
            var baseline = BaselineStore.Read(options.BaselinePath);
            BaselineMatcher.Apply(result, baseline, options.BaselinePath);
        }
    }
}
