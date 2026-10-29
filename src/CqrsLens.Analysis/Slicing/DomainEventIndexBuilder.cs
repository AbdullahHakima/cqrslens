namespace CqrsLens;

/// <summary>
/// Builds the second-level domain index: repository type → entity labels, notification wrapper
/// relationships, and domain member → raised-event mappings. These lookups are keyed by simple
/// type names, so ambiguous type names are excluded rather than attributed to an arbitrary module.
/// </summary>
internal static class DomainEventIndexBuilder
{
    internal static DomainEventIndex Build(
        List<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles,
        Dictionary<string, string> dbSetsByProperty,
        Dictionary<string, string> dbSetsByEntity,
        HashSet<string> dbContextTypeNames,
        Dictionary<string, List<CqrsNotification>> notificationsBySimple,
        Dictionary<string, List<HandlerInfo>> handlersByFull,
        Dictionary<string, List<NotificationHandlerInfo>> notifHandlersByFull,
        HashSet<string> ambiguousTypeNames)
    {
        var collector = new DomainEventIndexCollector(ambiguousTypeNames);

        DomainWrapperIndexer.Index(parsedFiles, notificationsBySimple, collector);
        DomainRepositoryIndexer.Index(
            parsedFiles,
            dbSetsByProperty,
            dbSetsByEntity,
            dbContextTypeNames,
            handlersByFull,
            notifHandlersByFull,
            collector);
        DomainMemberEventIndexer.Index(
            parsedFiles,
            notificationsBySimple,
            handlersByFull,
            notifHandlersByFull,
            collector);

        return collector.ToIndex();
    }
}
