namespace CqrsLens;

/// <summary>
/// Coordinates one handler-slice pass: build Entity Framework lookups, derive the domain event
/// and repository index, then enrich every handler and notification handler.
/// </summary>
internal static class HandlerDeepSliceAnalysis
{
    internal static void Run(
        List<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles,
        AnalysisResult result)
    {
        var mappings = DbSetMappingBuilder.Build(parsedFiles, result);

        var notificationsBySimple = result.Notifications
            .GroupBy(n => n.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var requestsBySimple = result.Requests
            .GroupBy(r => r.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var handlersByFull = result.Handlers
            .GroupBy(h => h.FullName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var notifHandlersByFull = result.NotificationHandlers
            .GroupBy(h => h.FullName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        // Member/repository lookups are keyed by simple type name. When two declarations share
        // one (e.g. Order in two modules), attributing an invocation to either is a guess.
        var ambiguousTypeNames = parsedFiles
            .SelectMany(file => file.Scan.Classes)
            .GroupBy(decl => decl.Name, StringComparer.Ordinal)
            .Where(group => group
                .Select(decl => decl.FullName)
                .Distinct(StringComparer.Ordinal)
                .Skip(1)
                .Any())
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);

        var domain = DomainEventIndexBuilder.Build(
            parsedFiles,
            mappings.DbSetsByProperty,
            mappings.DbSetsByEntity,
            mappings.DbContextTypeNames,
            notificationsBySimple,
            handlersByFull,
            notifHandlersByFull,
            ambiguousTypeNames);

        var index = new SliceIndex(
            mappings.DbSetsByProperty,
            mappings.DbSetsByEntity,
            mappings.DbContextTypeNames,
            domain.RepoEntitiesByType,
            notificationsBySimple,
            domain.WrapperNotificationsByInnerSimple,
            domain.MemberEvents,
            requestsBySimple);

        HandlerSliceEnricher.EnrichAll(parsedFiles, result, index, handlersByFull, notifHandlersByFull);
    }
}
