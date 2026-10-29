using static CqrsLens.JsonReportGenerator;

namespace CqrsLens;

/// <summary>Matches handlers by full name and request name.</summary>
internal static class HandlerDiffComparer
{
    public static HandlerDiffSection Compare(
        IReadOnlyList<JsonHandler> baseHandlers,
        IReadOnlyList<JsonHandler> headHandlers)
    {
        static string Key(JsonHandler handler) => DiffKey.Combine(handler.FullName, handler.RequestName);

        var baseKeys = baseHandlers.Select(Key).ToHashSet(StringComparer.Ordinal);
        var headKeys = headHandlers.Select(Key).ToHashSet(StringComparer.Ordinal);

        return new HandlerDiffSection
        {
            Added = headHandlers
                .Where(handler => !baseKeys.Contains(Key(handler)))
                .OrderBy(handler => handler.FullName, StringComparer.Ordinal)
                .ThenBy(handler => handler.RequestName, StringComparer.Ordinal)
                .ToList(),
            Removed = baseHandlers
                .Where(handler => !headKeys.Contains(Key(handler)))
                .OrderBy(handler => handler.FullName, StringComparer.Ordinal)
                .ThenBy(handler => handler.RequestName, StringComparer.Ordinal)
                .ToList(),
        };
    }
}
