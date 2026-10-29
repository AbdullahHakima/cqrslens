using static CqrsLens.JsonReportGenerator;

namespace CqrsLens;

/// <summary>Computes an architecture diff from two report documents or analysis results.</summary>
internal static class ArchitectureDiffCalculator
{
    internal static ArchitectureDiff Compare(AnalysisResult baseline, AnalysisResult current)
        => Compare(CreateDocument(baseline), CreateDocument(current));

    internal static ArchitectureDiff Compare(JsonReportDocument baseline, JsonReportDocument current)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(current);

        var baseErrors = ActiveChecks(baseline).Count(c => string.Equals(c.Severity, "error", StringComparison.OrdinalIgnoreCase));
        var baseWarnings = ActiveChecks(baseline).Count() - baseErrors;
        var headErrors = ActiveChecks(current).Count(c => string.Equals(c.Severity, "error", StringComparison.OrdinalIgnoreCase));
        var headWarnings = ActiveChecks(current).Count() - headErrors;

        var diff = new ArchitectureDiff
        {
            SchemaVersion = ArchitectureDiffer.CurrentSchemaVersion,
            ToolVersion = ToolInfo.Version,
            Scanner = current.Scanner,
            GeneratedAt = current.GeneratedAt,
            BaseRootName = baseline.RootName,
            HeadRootName = current.RootName,
            Watermark = current.Watermark,
            Summary = new DiffSummary
            {
                Projects = new DiffMetric(baseline.Projects.Count, current.Projects.Count),
                Requests = new DiffMetric(baseline.Requests.Count, current.Requests.Count),
                Handlers = new DiffMetric(baseline.Handlers.Count, current.Handlers.Count),
                Validators = new DiffMetric(baseline.Validators.Count, current.Validators.Count),
                Behaviors = new DiffMetric(baseline.Behaviors.Count, current.Behaviors.Count),
                DbContexts = new DiffMetric(baseline.DbContexts.Count, current.DbContexts.Count),
                Errors = new DiffMetric(baseErrors, headErrors),
                Warnings = new DiffMetric(baseWarnings, headWarnings),
            },
            Projects = CompareProjects(baseline.Projects, current.Projects),
            Requests = CompareRequests(baseline.Requests, current.Requests),
            Handlers = CompareHandlers(baseline.Handlers, current.Handlers),
            Validators = CompareValidators(baseline.Validators, current.Validators),
            Behaviors = CompareBehaviors(baseline.Behaviors, current.Behaviors),
            DbContexts = CompareDbContexts(baseline.DbContexts, current.DbContexts),
            Checks = CompareChecks(baseline.Checks, current.Checks),
        };

        diff.HasChanges =
            diff.Projects.HasChanges ||
            diff.Requests.HasChanges ||
            diff.Handlers.HasChanges ||
            diff.Validators.HasChanges ||
            diff.Behaviors.HasChanges ||
            diff.DbContexts.HasChanges ||
            diff.Checks.HasChanges;

        return diff;
    }

    private static IEnumerable<JsonCheckFinding> ActiveChecks(JsonReportDocument doc)
        => doc.BaselineApplied ? doc.Checks.Where(c => !c.Known) : doc.Checks;

    private static ProjectDiffSection CompareProjects(
        IReadOnlyList<JsonProject> baseProjects,
        IReadOnlyList<JsonProject> headProjects)
    {
        var section = new ProjectDiffSection();
        var baseByName = baseProjects
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var headByName = headProjects
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var h in headProjects.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!baseByName.TryGetValue(h.Name, out var b))
            {
                section.Added.Add(h);
                continue;
            }

            var layerChanged = !string.Equals(b.Layer, h.Layer, StringComparison.OrdinalIgnoreCase);
            var addedRefs = h.References
                .Except(b.References, StringComparer.OrdinalIgnoreCase)
                .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var removedRefs = b.References
                .Except(h.References, StringComparer.OrdinalIgnoreCase)
                .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var addedPkgs = h.Packages
                .Except(b.Packages, StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var removedPkgs = b.Packages
                .Except(h.Packages, StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (layerChanged || addedRefs.Count > 0 || removedRefs.Count > 0 || addedPkgs.Count > 0 || removedPkgs.Count > 0)
            {
                section.Modified.Add(new ModifiedProjectDiff
                {
                    Name = h.Name,
                    OldLayer = layerChanged ? b.Layer : null,
                    NewLayer = layerChanged ? h.Layer : null,
                    AddedReferences = addedRefs,
                    RemovedReferences = removedRefs,
                    AddedPackages = addedPkgs,
                    RemovedPackages = removedPkgs,
                });
            }
        }

        foreach (var b in baseProjects.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!headByName.ContainsKey(b.Name))
                section.Removed.Add(b);
        }

        return section;
    }

    private static RequestDiffSection CompareRequests(
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

        string RequestKey(JsonRequest r)
        {
            var duplicateInBase = baseCounts.TryGetValue(r.FullName, out var bc) && bc > 1;
            var duplicateInHead = headCounts.TryGetValue(r.FullName, out var hc) && hc > 1;
            return duplicateInBase || duplicateInHead
                ? $"{r.FullName}\u001f{r.Project ?? ""}\u001f{r.ResponseType ?? ""}"
                : r.FullName;
        }

        var baseByKey = baseRequests
            .GroupBy(RequestKey, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var headByKey = headRequests
            .GroupBy(RequestKey, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var h in headRequests.OrderBy(r => r.FullName, StringComparer.Ordinal))
        {
            var key = RequestKey(h);
            if (!baseByKey.TryGetValue(key, out var b))
            {
                section.Added.Add(h);
                continue;
            }

            var kindChanged = !string.Equals(b.Kind, h.Kind, StringComparison.Ordinal);
            var responseChanged = !string.Equals(b.ResponseType ?? "", h.ResponseType ?? "", StringComparison.Ordinal);
            var projectChanged = !string.Equals(b.Project ?? "", h.Project ?? "", StringComparison.Ordinal);
            var addedHandlers = h.Handlers.Except(b.Handlers, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var removedHandlers = b.Handlers.Except(h.Handlers, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var addedValidators = h.Validators.Except(b.Validators, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var removedValidators = b.Validators.Except(h.Validators, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();

            if (kindChanged || responseChanged || projectChanged ||
                addedHandlers.Count > 0 || removedHandlers.Count > 0 ||
                addedValidators.Count > 0 || removedValidators.Count > 0)
            {
                section.Modified.Add(new ModifiedRequestDiff
                {
                    FullName = h.FullName,
                    Name = h.Name,
                    Project = h.Project,
                    OldKind = kindChanged ? b.Kind : null,
                    NewKind = kindChanged ? h.Kind : null,
                    OldResponseType = responseChanged ? b.ResponseType : null,
                    NewResponseType = responseChanged ? h.ResponseType : null,
                    OldProject = projectChanged ? b.Project : null,
                    NewProject = projectChanged ? h.Project : null,
                    AddedHandlers = addedHandlers,
                    RemovedHandlers = removedHandlers,
                    AddedValidators = addedValidators,
                    RemovedValidators = removedValidators,
                    HeadSnapshot = h,
                });
            }
        }

        foreach (var b in baseRequests.OrderBy(r => r.FullName, StringComparer.Ordinal))
        {
            var key = RequestKey(b);
            if (!headByKey.ContainsKey(key))
                section.Removed.Add(b);
        }

        return section;
    }

    private static HandlerDiffSection CompareHandlers(
        IReadOnlyList<JsonHandler> baseHandlers,
        IReadOnlyList<JsonHandler> headHandlers)
    {
        static string Key(JsonHandler h) => $"{h.FullName}\u001f{h.RequestName}";
        var baseKeys = baseHandlers.Select(Key).ToHashSet(StringComparer.Ordinal);
        var headKeys = headHandlers.Select(Key).ToHashSet(StringComparer.Ordinal);

        return new HandlerDiffSection
        {
            Added = headHandlers
                .Where(h => !baseKeys.Contains(Key(h)))
                .OrderBy(h => h.FullName, StringComparer.Ordinal)
                .ThenBy(h => h.RequestName, StringComparer.Ordinal)
                .ToList(),
            Removed = baseHandlers
                .Where(h => !headKeys.Contains(Key(h)))
                .OrderBy(h => h.FullName, StringComparer.Ordinal)
                .ThenBy(h => h.RequestName, StringComparer.Ordinal)
                .ToList(),
        };
    }

    private static ValidatorDiffSection CompareValidators(
        IReadOnlyList<JsonValidator> baseValidators,
        IReadOnlyList<JsonValidator> headValidators)
    {
        static string Key(JsonValidator v) => $"{v.FullName}\u001f{v.TargetType}";
        var baseKeys = baseValidators.Select(Key).ToHashSet(StringComparer.Ordinal);
        var headKeys = headValidators.Select(Key).ToHashSet(StringComparer.Ordinal);

        return new ValidatorDiffSection
        {
            Added = headValidators
                .Where(v => !baseKeys.Contains(Key(v)))
                .OrderBy(v => v.FullName, StringComparer.Ordinal)
                .ThenBy(v => v.TargetType, StringComparer.Ordinal)
                .ToList(),
            Removed = baseValidators
                .Where(v => !headKeys.Contains(Key(v)))
                .OrderBy(v => v.FullName, StringComparer.Ordinal)
                .ThenBy(v => v.TargetType, StringComparer.Ordinal)
                .ToList(),
        };
    }

    private static BehaviorDiffSection CompareBehaviors(
        IReadOnlyList<JsonBehavior> baseBehaviors,
        IReadOnlyList<JsonBehavior> headBehaviors)
    {
        static string Key(JsonBehavior b) => $"{b.Name}\u001f{b.Project ?? ""}";
        var baseKeys = baseBehaviors.Select(Key).ToHashSet(StringComparer.Ordinal);
        var headKeys = headBehaviors.Select(Key).ToHashSet(StringComparer.Ordinal);

        return new BehaviorDiffSection
        {
            Added = headBehaviors
                .Where(b => !baseKeys.Contains(Key(b)))
                .OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Removed = baseBehaviors
                .Where(b => !headKeys.Contains(Key(b)))
                .OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
        };
    }

    private static DbContextDiffSection CompareDbContexts(
        IReadOnlyList<JsonDbContext> baseContexts,
        IReadOnlyList<JsonDbContext> headContexts)
    {
        var section = new DbContextDiffSection();
        static string Key(JsonDbContext c)
            => string.IsNullOrEmpty(c.Namespace) ? c.Name : $"{c.Namespace}.{c.Name}";
        static string DbSetKey(JsonDbSet s) => $"{s.Property} -> {s.Entity}";
        static string RelKey(JsonEntityRelationship r)
            => $"{CSharpScanner.SimpleName(r.PrincipalEntity)} -> {CSharpScanner.SimpleName(r.DependentEntity)} ({r.Kind})";

        var baseByKey = baseContexts
            .GroupBy(Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var headByKey = headContexts
            .GroupBy(Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var h in headContexts.OrderBy(Key, StringComparer.Ordinal))
        {
            var key = Key(h);
            if (!baseByKey.TryGetValue(key, out var b))
            {
                section.Added.Add(h);
                continue;
            }

            var bSets = b.DbSets.Select(DbSetKey).ToList();
            var hSets = h.DbSets.Select(DbSetKey).ToList();
            var addedSets = hSets.Except(bSets, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var removedSets = bSets.Except(hSets, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();

            var bEntities = b.Entities.Select(e => e.FullName).ToList();
            var hEntities = h.Entities.Select(e => e.FullName).ToList();
            var addedEntities = hEntities.Except(bEntities, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var removedEntities = bEntities.Except(hEntities, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();

            var bRels = b.Relationships.Select(RelKey).ToList();
            var hRels = h.Relationships.Select(RelKey).ToList();
            var addedRels = hRels.Except(bRels, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var removedRels = bRels.Except(hRels, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();

            if (addedSets.Count > 0 || removedSets.Count > 0 ||
                addedEntities.Count > 0 || removedEntities.Count > 0 ||
                addedRels.Count > 0 || removedRels.Count > 0)
            {
                section.Modified.Add(new ModifiedDbContextDiff
                {
                    Name = h.Name,
                    Project = h.Project,
                    AddedDbSets = addedSets,
                    RemovedDbSets = removedSets,
                    AddedEntities = addedEntities,
                    RemovedEntities = removedEntities,
                    AddedRelationships = addedRels,
                    RemovedRelationships = removedRels,
                });
            }
        }

        foreach (var b in baseContexts.OrderBy(Key, StringComparer.Ordinal))
        {
            if (!headByKey.ContainsKey(Key(b)))
                section.Removed.Add(b);
        }

        return section;
    }

    private static CheckDiffSection CompareChecks(
        IReadOnlyList<JsonCheckFinding> baseChecks,
        IReadOnlyList<JsonCheckFinding> headChecks)
    {
        static string Key(JsonCheckFinding c)
            => Baseline.CreateKey(c.Code, c.Target, c.Location?.File);

        var activeBase = baseChecks.Where(c => !c.Known).ToList();
        var activeHead = headChecks.Where(c => !c.Known).ToList();

        var baseRemaining = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var c in activeBase)
        {
            var key = Key(c);
            baseRemaining[key] = baseRemaining.TryGetValue(key, out var count) ? count + 1 : 1;
        }

        var newFindings = new List<JsonCheckFinding>();
        foreach (var c in activeHead)
        {
            var key = Key(c);
            if (baseRemaining.TryGetValue(key, out var count) && count > 0)
                baseRemaining[key] = count - 1;
            else
                newFindings.Add(c);
        }

        var headRemaining = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var c in activeHead)
        {
            var key = Key(c);
            headRemaining[key] = headRemaining.TryGetValue(key, out var count) ? count + 1 : 1;
        }

        var resolvedFindings = new List<JsonCheckFinding>();
        foreach (var c in activeBase)
        {
            var key = Key(c);
            if (headRemaining.TryGetValue(key, out var count) && count > 0)
                headRemaining[key] = count - 1;
            else
                resolvedFindings.Add(c);
        }

        return new CheckDiffSection
        {
            NewFindings = newFindings,
            ResolvedFindings = resolvedFindings,
        };
    }
}