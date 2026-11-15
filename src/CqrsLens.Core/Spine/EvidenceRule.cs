namespace CqrsLens;

/// <summary>
/// Static evidence rules that admit components, roles, and relations into the spine (F0 §3.1, §4, §4.3, §4.4).
/// </summary>
public enum EvidenceRule
{
    ProjectReference,
    RequestMarker,
    HandlerMarker,
    ValidatorMarker,
    BehaviorClosedBinding,
    BehaviorOpenGenericRegistration,
    NotificationMarker,
    NotificationHandlerMarker,
    DbContextInheritance,
    EntityDiscovery,
    EndpointMapping,
    DispatchCallSite,
    PublishCallSite,
    EndpointDispatch,
    DirectDbContextReference,
    ResolvedDbContextDependency,
    ServiceDeclarationCandidate,
    ServiceRegistrationBinding,
    ServiceConfirmedBinding,
    InjectedMemberInvocation,
    DeepSliceDataAccess,
    EntityNavigation,
    EntityAnnotation,
    EntityFluentConfiguration,
}
