namespace CqrsLens;

/// <summary>Aggregate counts used to describe and style one CQRS diagram.</summary>
internal readonly record struct CqrsStats(
    int Handlers,
    int Validators,
    int MissingHandlers,
    int MissingValidators)
{
    internal static CqrsStats Create(
        IReadOnlyList<CqrsRequest> requests,
        HashSet<string> missingHandlers,
        HashSet<string> missingValidators)
    {
        var requestNames = requests
            .Select(r => r.FullName)
            .ToHashSet(StringComparer.Ordinal);
        return new CqrsStats(
            requests.SelectMany(r => r.Handlers).Distinct(StringComparer.Ordinal).Count(),
            requests.SelectMany(r => r.Validators).Distinct(StringComparer.Ordinal).Count(),
            missingHandlers.Count(requestNames.Contains),
            missingValidators.Count(requestNames.Contains));
    }
}
