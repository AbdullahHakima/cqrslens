namespace CqrsLens;

/// <summary>Kinds of named shape a claim subject can be resolved against.</summary>
internal enum ClaimSubjectKind
{
    Request,
    Handler,
    NotificationHandler,
    Validator,
    Behavior,
    DbContext,
    Entity,
    Project,
    Notification,
}
