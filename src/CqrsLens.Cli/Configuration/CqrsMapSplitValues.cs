namespace CqrsLens;

/// <summary>Parses the accepted <c>--cqrs-map-split</c> / <c>cqrsMapSplit</c> values.</summary>
internal static class CqrsMapSplitValues
{
    internal const string Accepted = "count, feature or unsplit";

    internal static bool TryParse(string? value, out CqrsMapSplitMode mode)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "count":
                mode = CqrsMapSplitMode.Count;
                return true;
            case "feature":
                mode = CqrsMapSplitMode.Feature;
                return true;
            case "unsplit":
                mode = CqrsMapSplitMode.Unsplit;
                return true;
            default:
                mode = CqrsMapSplitMode.Count;
                return false;
        }
    }
}
