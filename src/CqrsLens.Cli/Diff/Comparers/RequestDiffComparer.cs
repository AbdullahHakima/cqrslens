namespace CqrsLens;

/// <summary>Matches requests by full name (with a composite key for duplicates) and diffs their surface.</summary>
internal static class RequestDiffComparer
{
    public static RequestDiffSection Compare(
        IReadOnlyList<JsonRequest> baseRequests,
        IReadOnlyList<JsonRequest> headRequests)
    {
        var section = new RequestDiffSection();

        var baseCounts = baseRequests
            .GroupBy(r => r.FullName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var headCounts = headRequests
            .GroupBy(r => r.FullName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        string Key(JsonRequest request)
        {
            var duplicateInBase = baseCounts.TryGetValue(request.FullName, out var baseCount) && baseCount > 1;
            var duplicateInHead = headCounts.TryGetValue(request.FullName, out var headCount) && headCount > 1;
            return duplicateInBase || duplicateInHead
                ? DiffKey.Combine(request.FullName, request.Project, request.ResponseType)
                : request.FullName;
        }

        var baseByKey = baseRequests
            .GroupBy(Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var headByKey = headRequests
            .GroupBy(Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var head in headRequests.OrderBy(r => r.FullName, StringComparer.Ordinal))
        {
            var key = Key(head);
            if (!baseByKey.TryGetValue(key, out var baseline))
            {
                section.Added.Add(head);
                continue;
            }

            var kindChanged = !string.Equals(baseline.Kind, head.Kind, StringComparison.Ordinal);
            var responseChanged = !string.Equals(
                baseline.ResponseType ?? "",
                head.ResponseType ?? "",
                StringComparison.Ordinal);
            var projectChanged = !string.Equals(
                baseline.Project ?? "",
                head.Project ?? "",
                StringComparison.Ordinal);
            var addedHandlers = Difference(head.Handlers, baseline.Handlers);
            var removedHandlers = Difference(baseline.Handlers, head.Handlers);
            var addedValidators = Difference(head.Validators, baseline.Validators);
            var removedValidators = Difference(baseline.Validators, head.Validators);

            if (!kindChanged && !responseChanged && !projectChanged &&
                addedHandlers.Count == 0 && removedHandlers.Count == 0 &&
                addedValidators.Count == 0 && removedValidators.Count == 0)
            {
                continue;
            }

            section.Modified.Add(new ModifiedRequestDiff
            {
                FullName = head.FullName,
                Name = head.Name,
                Project = head.Project,
                OldKind = kindChanged ? baseline.Kind : null,
                NewKind = kindChanged ? head.Kind : null,
                OldResponseType = responseChanged ? baseline.ResponseType : null,
                NewResponseType = responseChanged ? head.ResponseType : null,
                OldProject = projectChanged ? baseline.Project : null,
                NewProject = projectChanged ? head.Project : null,
                AddedHandlers = addedHandlers,
                RemovedHandlers = removedHandlers,
                AddedValidators = addedValidators,
                RemovedValidators = removedValidators,
                HeadSnapshot = head,
            });
        }

        foreach (var baseline in baseRequests.OrderBy(r => r.FullName, StringComparer.Ordinal))
        {
            if (!headByKey.ContainsKey(Key(baseline)))
                section.Removed.Add(baseline);
        }

        return section;
    }

    private static List<string> Difference(IReadOnlyList<string> left, IReadOnlyList<string> right)
        => left.Except(right, StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList();
}
