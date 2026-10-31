namespace CqrsLens;

/// <summary>
/// Enriches handler and notification-handler shapes with constructor dependencies, touched
/// DbSets/entities, published notifications and dispatched requests. All receiver-based lookups
/// fail closed: a member is attributed only to the type the source actually declares or infers.
/// </summary>
internal static class HandlerSliceEnricher
{
    internal static void EnrichAll(
        List<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles,
        AnalysisResult result,
        SliceIndex index,
        Dictionary<string, List<HandlerInfo>> handlersByFull,
        Dictionary<string, List<NotificationHandlerInfo>> notifHandlersByFull)
    {
        foreach (var (file, scan, mask) in parsedFiles)
        {
            foreach (var decl in scan.Classes)
            {
                if (handlersByFull.TryGetValue(decl.FullName, out var reqHandlerCandidates))
                {
                    var handler = DeepSliceCandidateResolver.PickHandler(reqHandlerCandidates, file.ProjectName);
                    if (handler is not null)
                    {
                        Enrich(
                            decl, file, scan, mask,
                            handler.Dependencies,
                            handler.TouchedEntities,
                            handler.PublishedNotifications,
                            handler.DispatchedRequests,
                            selfRequestName: handler.RequestName,
                            index);
                    }
                }

                if (notifHandlersByFull.TryGetValue(decl.FullName, out var notifHandlerCandidates))
                {
                    var notifHandler = DeepSliceCandidateResolver.PickNotificationHandler(notifHandlerCandidates, file.ProjectName);
                    if (notifHandler is not null)
                    {
                        Enrich(
                            decl, file, scan, mask,
                            notifHandler.Dependencies,
                            notifHandler.TouchedEntities,
                            notifHandler.PublishedNotifications,
                            notifHandler.DispatchedRequests,
                            selfRequestName: null,
                            index);
                    }
                }
            }
        }

        HandlerEffectRollup.Apply(result);
    }

    private static void Enrich(
        CSharpScanner.ClassDecl decl,
        SourceFile file,
        CSharpScanner.FileScan scan,
        SourceMask mask,
        List<string> dependencies,
        List<string> touchedEntities,
        List<string> publishedNotifications,
        List<string> dispatchedRequests,
        string? selfRequestName,
        SliceIndex index)
    {
        var context = new HandlerEffectContext(
            decl, file, scan, mask, index,
            dependencies,
            touchedEntities,
            publishedNotifications,
            dispatchedRequests,
            selfRequestName);
        HandlerTargetEnricher.Enrich(context);
    }
}
