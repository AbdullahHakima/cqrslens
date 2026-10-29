namespace CqrsLens;

/// <summary>Matches validators by full name and target type.</summary>
internal static class ValidatorDiffComparer
{
    public static ValidatorDiffSection Compare(
        IReadOnlyList<JsonValidator> baseValidators,
        IReadOnlyList<JsonValidator> headValidators)
    {
        static string Key(JsonValidator validator) => DiffKey.Combine(validator.FullName, validator.TargetType);

        var baseKeys = baseValidators.Select(Key).ToHashSet(StringComparer.Ordinal);
        var headKeys = headValidators.Select(Key).ToHashSet(StringComparer.Ordinal);

        return new ValidatorDiffSection
        {
            Added = headValidators
                .Where(validator => !baseKeys.Contains(Key(validator)))
                .OrderBy(validator => validator.FullName, StringComparer.Ordinal)
                .ThenBy(validator => validator.TargetType, StringComparer.Ordinal)
                .ToList(),
            Removed = baseValidators
                .Where(validator => !headKeys.Contains(Key(validator)))
                .OrderBy(validator => validator.FullName, StringComparer.Ordinal)
                .ThenBy(validator => validator.TargetType, StringComparer.Ordinal)
                .ToList(),
        };
    }
}
