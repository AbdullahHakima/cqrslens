namespace CqrsLens;

/// <summary>Matches DbContexts by namespace-qualified name and diffs DbSets, entities and relationships.</summary>
internal static class DbContextDiffComparer
{
    public static DbContextDiffSection Compare(
        IReadOnlyList<JsonDbContext> baseContexts,
        IReadOnlyList<JsonDbContext> headContexts)
    {
        var section = new DbContextDiffSection();

        static string Key(JsonDbContext context)
            => string.IsNullOrEmpty(context.Namespace) ? context.Name : $"{context.Namespace}.{context.Name}";
        static string DbSetKey(JsonDbSet set) => $"{set.Property} -> {set.Entity}";
        static string RelationshipKey(JsonEntityRelationship relationship)
            => $"{CSharpScanner.SimpleName(relationship.PrincipalEntity)} -> " +
               $"{CSharpScanner.SimpleName(relationship.DependentEntity)} ({relationship.Kind})";

        var baseByKey = baseContexts
            .GroupBy(Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var headByKey = headContexts
            .GroupBy(Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var head in headContexts.OrderBy(Key, StringComparer.Ordinal))
        {
            var key = Key(head);
            if (!baseByKey.TryGetValue(key, out var baseline))
            {
                section.Added.Add(head);
                continue;
            }

            var addedDbSets = Difference(head.DbSets.Select(DbSetKey), baseline.DbSets.Select(DbSetKey));
            var removedDbSets = Difference(baseline.DbSets.Select(DbSetKey), head.DbSets.Select(DbSetKey));
            var addedEntities = Difference(
                head.Entities.Select(entity => entity.FullName),
                baseline.Entities.Select(entity => entity.FullName));
            var removedEntities = Difference(
                baseline.Entities.Select(entity => entity.FullName),
                head.Entities.Select(entity => entity.FullName));
            var addedRelationships = Difference(
                head.Relationships.Select(RelationshipKey),
                baseline.Relationships.Select(RelationshipKey));
            var removedRelationships = Difference(
                baseline.Relationships.Select(RelationshipKey),
                head.Relationships.Select(RelationshipKey));

            if (addedDbSets.Count == 0 && removedDbSets.Count == 0 &&
                addedEntities.Count == 0 && removedEntities.Count == 0 &&
                addedRelationships.Count == 0 && removedRelationships.Count == 0)
            {
                continue;
            }

            section.Modified.Add(new ModifiedDbContextDiff
            {
                Name = head.Name,
                Project = head.Project,
                AddedDbSets = addedDbSets,
                RemovedDbSets = removedDbSets,
                AddedEntities = addedEntities,
                RemovedEntities = removedEntities,
                AddedRelationships = addedRelationships,
                RemovedRelationships = removedRelationships,
            });
        }

        foreach (var baseline in baseContexts.OrderBy(Key, StringComparer.Ordinal))
        {
            if (!headByKey.ContainsKey(Key(baseline)))
                section.Removed.Add(baseline);
        }

        return section;
    }

    private static List<string> Difference(IEnumerable<string> left, IEnumerable<string> right)
        => left.Except(right, StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList();
}
