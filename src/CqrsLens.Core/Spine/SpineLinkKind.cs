namespace CqrsLens;

internal enum SpineLinkKind
{
    ProjectReferences,
    RequestHandlers,
    RequestValidators,
    RequestEntrypoints,
    RequestTouchedEntities,
    RequestPublishedNotifications,
    RequestDispatchedRequests,
    NotificationHandlers,
    HandlerTouchedEntities,
    HandlerPublishedNotifications,
    HandlerDispatchedRequests,
}
