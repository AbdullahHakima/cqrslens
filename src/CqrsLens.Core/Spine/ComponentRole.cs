namespace CqrsLens;

/// <summary>
/// Tier 1 active architectural component roles in F1 (F0 §3.1).
/// Future roles planned for F2 (Service, Repository, Controller, BackgroundWorker) are not admitted in F1.
/// </summary>
public enum ComponentRole
{
    Request,
    Handler,
    Notification,
    NotificationHandler,
    Validator,
    Behavior,
    DbContext,
    Entity,
    Endpoint,
    Controller,
    Service,
    Repository,
}
