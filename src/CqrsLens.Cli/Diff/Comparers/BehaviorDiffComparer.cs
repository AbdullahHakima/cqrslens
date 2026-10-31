namespace CqrsLens;

/// <summary>Matches pipeline behaviors by name and project.</summary>
internal static class BehaviorDiffComparer
{
    public static BehaviorDiffSection Compare(
        IReadOnlyList<JsonBehavior> baseBehaviors,
        IReadOnlyList<JsonBehavior> headBehaviors)
    {
        static string Key(JsonBehavior behavior) => DiffKey.Combine(behavior.Name, behavior.Project);

        var baseKeys = baseBehaviors.Select(Key).ToHashSet(StringComparer.Ordinal);
        var headKeys = headBehaviors.Select(Key).ToHashSet(StringComparer.Ordinal);

        return new BehaviorDiffSection
        {
            Added = headBehaviors
                .Where(behavior => !baseKeys.Contains(Key(behavior)))
                .OrderBy(behavior => behavior.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Removed = baseBehaviors
                .Where(behavior => !headKeys.Contains(Key(behavior)))
                .OrderBy(behavior => behavior.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
        };
    }
}
