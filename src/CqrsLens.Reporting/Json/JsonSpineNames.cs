using System.Text.Json;

namespace CqrsLens;

/// <summary>Precomputed camelCase property names and enum tokens for spine JSON serialization.</summary>
internal static class JsonSpineNames
{
    // Relation properties
    internal static readonly JsonEncodedText KindProp = JsonEncodedText.Encode("kind");
    internal static readonly JsonEncodedText FromProp = JsonEncodedText.Encode("from");
    internal static readonly JsonEncodedText ToProp = JsonEncodedText.Encode("to");
    internal static readonly JsonEncodedText ViaProp = JsonEncodedText.Encode("via");
    internal static readonly JsonEncodedText MethodProp = JsonEncodedText.Encode("method");
    internal static readonly JsonEncodedText StrengthProp = JsonEncodedText.Encode("strength");
    internal static readonly JsonEncodedText OccurrenceCountProp = JsonEncodedText.Encode("occurrenceCount");
    internal static readonly JsonEncodedText OccurrencesProp = JsonEncodedText.Encode("occurrences");

    // NodeReference properties
    internal static readonly JsonEncodedText KeyProp = JsonEncodedText.Encode("key");
    internal static readonly JsonEncodedText NameProp = JsonEncodedText.Encode("name");
    internal static readonly JsonEncodedText ComponentIdProp = JsonEncodedText.Encode("componentId");
    internal static readonly JsonEncodedText EndpointIdProp = JsonEncodedText.Encode("endpointId");
    internal static readonly JsonEncodedText ProjectIdProp = JsonEncodedText.Encode("projectId");
    internal static readonly JsonEncodedText SymbolIdProp = JsonEncodedText.Encode("symbolId");
    internal static readonly JsonEncodedText MemberProp = JsonEncodedText.Encode("member");
    internal static readonly JsonEncodedText ReasonProp = JsonEncodedText.Encode("reason");

    // Evidence properties
    internal static readonly JsonEncodedText RuleProp = JsonEncodedText.Encode("rule");
    internal static readonly JsonEncodedText OriginProp = JsonEncodedText.Encode("origin");
    internal static readonly JsonEncodedText LocationProp = JsonEncodedText.Encode("location");
    internal static readonly JsonEncodedText LocationOmittedReasonProp = JsonEncodedText.Encode("locationOmittedReason");
    internal static readonly JsonEncodedText ResolutionSemanticsProp = JsonEncodedText.Encode("resolutionSemantics");
    internal static readonly JsonEncodedText ScopeProjectProp = JsonEncodedText.Encode("scopeProject");
    internal static readonly JsonEncodedText TestProjectProp = JsonEncodedText.Encode("testProject");

    // Location properties
    internal static readonly JsonEncodedText FileProp = JsonEncodedText.Encode("file");
    internal static readonly JsonEncodedText LineProp = JsonEncodedText.Encode("line");
    internal static readonly JsonEncodedText ColumnProp = JsonEncodedText.Encode("column");

    // RelationKind tokens
    private static readonly JsonEncodedText RelReferences = JsonEncodedText.Encode("references");
    private static readonly JsonEncodedText RelServes = JsonEncodedText.Encode("serves");
    private static readonly JsonEncodedText RelValidates = JsonEncodedText.Encode("validates");
    private static readonly JsonEncodedText RelWraps = JsonEncodedText.Encode("wraps");
    private static readonly JsonEncodedText RelDispatches = JsonEncodedText.Encode("dispatches");
    private static readonly JsonEncodedText RelPublishes = JsonEncodedText.Encode("publishes");
    private static readonly JsonEncodedText RelHandlesNotification = JsonEncodedText.Encode("handlesNotification");
    private static readonly JsonEncodedText RelUsesData = JsonEncodedText.Encode("usesData");
    private static readonly JsonEncodedText RelUsesDataResolved = JsonEncodedText.Encode("usesDataResolved");
    private static readonly JsonEncodedText RelResolvesTo = JsonEncodedText.Encode("resolvesTo");
    private static readonly JsonEncodedText RelCalls = JsonEncodedText.Encode("calls");
    private static readonly JsonEncodedText RelTouchesData = JsonEncodedText.Encode("touchesData");
    private static readonly JsonEncodedText RelRelates = JsonEncodedText.Encode("relates");

    // NodeReferenceKind tokens
    private static readonly JsonEncodedText NodeProject = JsonEncodedText.Encode("project");
    private static readonly JsonEncodedText NodeLayer = JsonEncodedText.Encode("layer");
    private static readonly JsonEncodedText NodeComponent = JsonEncodedText.Encode("component");
    private static readonly JsonEncodedText NodeEndpoint = JsonEncodedText.Encode("endpoint");
    private static readonly JsonEncodedText NodeInterface = JsonEncodedText.Encode("interface");
    private static readonly JsonEncodedText NodeMethod = JsonEncodedText.Encode("method");
    private static readonly JsonEncodedText NodeMember = JsonEncodedText.Encode("member");
    private static readonly JsonEncodedText NodeType = JsonEncodedText.Encode("type");
    private static readonly JsonEncodedText NodeExternal = JsonEncodedText.Encode("external");
    private static readonly JsonEncodedText NodeUnresolved = JsonEncodedText.Encode("unresolved");

    // EvidenceStrength tokens
    private static readonly JsonEncodedText StrProven = JsonEncodedText.Encode("proven");
    private static readonly JsonEncodedText StrResolved = JsonEncodedText.Encode("resolved");
    private static readonly JsonEncodedText StrInferred = JsonEncodedText.Encode("inferred");

    // EvidenceOrigin tokens
    private static readonly JsonEncodedText OrigDeclaration = JsonEncodedText.Encode("declaration");
    private static readonly JsonEncodedText OrigRegistration = JsonEncodedText.Encode("registration");
    private static readonly JsonEncodedText OrigCallSite = JsonEncodedText.Encode("callSite");
    private static readonly JsonEncodedText OrigMappingOrAttribute = JsonEncodedText.Encode("mappingOrAttribute");
    private static readonly JsonEncodedText OrigProjectFile = JsonEncodedText.Encode("projectFile");
    private static readonly JsonEncodedText OrigConfiguration = JsonEncodedText.Encode("configuration");

    // ResolutionSemantics tokens
    private static readonly JsonEncodedText SemStaticCandidate = JsonEncodedText.Encode("staticCandidate");
    private static readonly JsonEncodedText SemDeclaredBinding = JsonEncodedText.Encode("declaredBinding");
    private static readonly JsonEncodedText SemConfirmed = JsonEncodedText.Encode("confirmed");

    // EvidenceRule tokens
    private static readonly JsonEncodedText RuleProjectReference = JsonEncodedText.Encode("projectReference");
    private static readonly JsonEncodedText RuleRequestMarker = JsonEncodedText.Encode("requestMarker");
    private static readonly JsonEncodedText RuleHandlerMarker = JsonEncodedText.Encode("handlerMarker");
    private static readonly JsonEncodedText RuleValidatorMarker = JsonEncodedText.Encode("validatorMarker");
    private static readonly JsonEncodedText RuleBehaviorClosedBinding = JsonEncodedText.Encode("behaviorClosedBinding");
    private static readonly JsonEncodedText RuleBehaviorOpenGenericRegistration = JsonEncodedText.Encode("behaviorOpenGenericRegistration");
    private static readonly JsonEncodedText RuleNotificationMarker = JsonEncodedText.Encode("notificationMarker");
    private static readonly JsonEncodedText RuleNotificationHandlerMarker = JsonEncodedText.Encode("notificationHandlerMarker");
    private static readonly JsonEncodedText RuleDbContextInheritance = JsonEncodedText.Encode("dbContextInheritance");
    private static readonly JsonEncodedText RuleEntityDiscovery = JsonEncodedText.Encode("entityDiscovery");
    private static readonly JsonEncodedText RuleEndpointMapping = JsonEncodedText.Encode("endpointMapping");
    private static readonly JsonEncodedText RuleDispatchCallSite = JsonEncodedText.Encode("dispatchCallSite");
    private static readonly JsonEncodedText RulePublishCallSite = JsonEncodedText.Encode("publishCallSite");
    private static readonly JsonEncodedText RuleEndpointDispatch = JsonEncodedText.Encode("endpointDispatch");
    private static readonly JsonEncodedText RuleDirectDbContextReference = JsonEncodedText.Encode("directDbContextReference");
    private static readonly JsonEncodedText RuleResolvedDbContextDependency = JsonEncodedText.Encode("resolvedDbContextDependency");
    private static readonly JsonEncodedText RuleServiceDeclarationCandidate = JsonEncodedText.Encode("serviceDeclarationCandidate");
    private static readonly JsonEncodedText RuleServiceRegistrationBinding = JsonEncodedText.Encode("serviceRegistrationBinding");
    private static readonly JsonEncodedText RuleServiceConfirmedBinding = JsonEncodedText.Encode("serviceConfirmedBinding");
    private static readonly JsonEncodedText RuleInjectedMemberInvocation = JsonEncodedText.Encode("injectedMemberInvocation");
    private static readonly JsonEncodedText RuleDeepSliceDataAccess = JsonEncodedText.Encode("deepSliceDataAccess");
    private static readonly JsonEncodedText RuleEntityNavigation = JsonEncodedText.Encode("entityNavigation");
    private static readonly JsonEncodedText RuleEntityAnnotation = JsonEncodedText.Encode("entityAnnotation");
    private static readonly JsonEncodedText RuleEntityFluentConfiguration = JsonEncodedText.Encode("entityFluentConfiguration");

    internal static string RelationKindName(RelationKind kind) => kind switch
    {
        RelationKind.References => "references",
        RelationKind.Serves => "serves",
        RelationKind.Validates => "validates",
        RelationKind.Wraps => "wraps",
        RelationKind.Dispatches => "dispatches",
        RelationKind.Publishes => "publishes",
        RelationKind.HandlesNotification => "handlesNotification",
        RelationKind.UsesData => "usesData",
        RelationKind.UsesDataResolved => "usesDataResolved",
        RelationKind.ResolvesTo => "resolvesTo",
        RelationKind.Calls => "calls",
        RelationKind.TouchesData => "touchesData",
        RelationKind.Relates => "relates",
        _ => JsonNamingPolicy.CamelCase.ConvertName(kind.ToString()),
    };

    internal static JsonEncodedText RelationKindEncoded(RelationKind kind) => kind switch
    {
        RelationKind.References => RelReferences,
        RelationKind.Serves => RelServes,
        RelationKind.Validates => RelValidates,
        RelationKind.Wraps => RelWraps,
        RelationKind.Dispatches => RelDispatches,
        RelationKind.Publishes => RelPublishes,
        RelationKind.HandlesNotification => RelHandlesNotification,
        RelationKind.UsesData => RelUsesData,
        RelationKind.UsesDataResolved => RelUsesDataResolved,
        RelationKind.ResolvesTo => RelResolvesTo,
        RelationKind.Calls => RelCalls,
        RelationKind.TouchesData => RelTouchesData,
        RelationKind.Relates => RelRelates,
        _ => JsonEncodedText.Encode(JsonNamingPolicy.CamelCase.ConvertName(kind.ToString())),
    };

    internal static string NodeReferenceKindName(NodeReferenceKind kind) => kind switch
    {
        NodeReferenceKind.Project => "project",
        NodeReferenceKind.Layer => "layer",
        NodeReferenceKind.Component => "component",
        NodeReferenceKind.Endpoint => "endpoint",
        NodeReferenceKind.Interface => "interface",
        NodeReferenceKind.Method => "method",
        NodeReferenceKind.Member => "member",
        NodeReferenceKind.Type => "type",
        NodeReferenceKind.External => "external",
        NodeReferenceKind.Unresolved => "unresolved",
        _ => JsonNamingPolicy.CamelCase.ConvertName(kind.ToString()),
    };

    internal static JsonEncodedText NodeReferenceKindEncoded(NodeReferenceKind kind) => kind switch
    {
        NodeReferenceKind.Project => NodeProject,
        NodeReferenceKind.Layer => NodeLayer,
        NodeReferenceKind.Component => NodeComponent,
        NodeReferenceKind.Endpoint => NodeEndpoint,
        NodeReferenceKind.Interface => NodeInterface,
        NodeReferenceKind.Method => NodeMethod,
        NodeReferenceKind.Member => NodeMember,
        NodeReferenceKind.Type => NodeType,
        NodeReferenceKind.External => NodeExternal,
        NodeReferenceKind.Unresolved => NodeUnresolved,
        _ => JsonEncodedText.Encode(JsonNamingPolicy.CamelCase.ConvertName(kind.ToString())),
    };

    internal static string EvidenceStrengthName(EvidenceStrength strength) => strength switch
    {
        EvidenceStrength.Proven => "proven",
        EvidenceStrength.Resolved => "resolved",
        EvidenceStrength.Inferred => "inferred",
        _ => JsonNamingPolicy.CamelCase.ConvertName(strength.ToString()),
    };

    internal static JsonEncodedText EvidenceStrengthEncoded(EvidenceStrength strength) => strength switch
    {
        EvidenceStrength.Proven => StrProven,
        EvidenceStrength.Resolved => StrResolved,
        EvidenceStrength.Inferred => StrInferred,
        _ => JsonEncodedText.Encode(JsonNamingPolicy.CamelCase.ConvertName(strength.ToString())),
    };

    internal static string EvidenceOriginName(EvidenceOrigin origin) => origin switch
    {
        EvidenceOrigin.Declaration => "declaration",
        EvidenceOrigin.Registration => "registration",
        EvidenceOrigin.CallSite => "callSite",
        EvidenceOrigin.MappingOrAttribute => "mappingOrAttribute",
        EvidenceOrigin.ProjectFile => "projectFile",
        EvidenceOrigin.Configuration => "configuration",
        _ => JsonNamingPolicy.CamelCase.ConvertName(origin.ToString()),
    };

    internal static JsonEncodedText EvidenceOriginEncoded(EvidenceOrigin origin) => origin switch
    {
        EvidenceOrigin.Declaration => OrigDeclaration,
        EvidenceOrigin.Registration => OrigRegistration,
        EvidenceOrigin.CallSite => OrigCallSite,
        EvidenceOrigin.MappingOrAttribute => OrigMappingOrAttribute,
        EvidenceOrigin.ProjectFile => OrigProjectFile,
        EvidenceOrigin.Configuration => OrigConfiguration,
        _ => JsonEncodedText.Encode(JsonNamingPolicy.CamelCase.ConvertName(origin.ToString())),
    };

    internal static string ResolutionSemanticsName(ResolutionSemantics semantics) => semantics switch
    {
        ResolutionSemantics.StaticCandidate => "staticCandidate",
        ResolutionSemantics.DeclaredBinding => "declaredBinding",
        ResolutionSemantics.Confirmed => "confirmed",
        _ => JsonNamingPolicy.CamelCase.ConvertName(semantics.ToString()),
    };

    internal static JsonEncodedText ResolutionSemanticsEncoded(ResolutionSemantics semantics) => semantics switch
    {
        ResolutionSemantics.StaticCandidate => SemStaticCandidate,
        ResolutionSemantics.DeclaredBinding => SemDeclaredBinding,
        ResolutionSemantics.Confirmed => SemConfirmed,
        _ => JsonEncodedText.Encode(JsonNamingPolicy.CamelCase.ConvertName(semantics.ToString())),
    };

    internal static string EvidenceRuleName(EvidenceRule rule) => rule switch
    {
        EvidenceRule.ProjectReference => "projectReference",
        EvidenceRule.RequestMarker => "requestMarker",
        EvidenceRule.HandlerMarker => "handlerMarker",
        EvidenceRule.ValidatorMarker => "validatorMarker",
        EvidenceRule.BehaviorClosedBinding => "behaviorClosedBinding",
        EvidenceRule.BehaviorOpenGenericRegistration => "behaviorOpenGenericRegistration",
        EvidenceRule.NotificationMarker => "notificationMarker",
        EvidenceRule.NotificationHandlerMarker => "notificationHandlerMarker",
        EvidenceRule.DbContextInheritance => "dbContextInheritance",
        EvidenceRule.EntityDiscovery => "entityDiscovery",
        EvidenceRule.EndpointMapping => "endpointMapping",
        EvidenceRule.DispatchCallSite => "dispatchCallSite",
        EvidenceRule.PublishCallSite => "publishCallSite",
        EvidenceRule.EndpointDispatch => "endpointDispatch",
        EvidenceRule.DirectDbContextReference => "directDbContextReference",
        EvidenceRule.ResolvedDbContextDependency => "resolvedDbContextDependency",
        EvidenceRule.ServiceDeclarationCandidate => "serviceDeclarationCandidate",
        EvidenceRule.ServiceRegistrationBinding => "serviceRegistrationBinding",
        EvidenceRule.ServiceConfirmedBinding => "serviceConfirmedBinding",
        EvidenceRule.InjectedMemberInvocation => "injectedMemberInvocation",
        EvidenceRule.DeepSliceDataAccess => "deepSliceDataAccess",
        EvidenceRule.EntityNavigation => "entityNavigation",
        EvidenceRule.EntityAnnotation => "entityAnnotation",
        EvidenceRule.EntityFluentConfiguration => "entityFluentConfiguration",
        _ => JsonNamingPolicy.CamelCase.ConvertName(rule.ToString()),
    };

    internal static JsonEncodedText EvidenceRuleEncoded(EvidenceRule rule) => rule switch
    {
        EvidenceRule.ProjectReference => RuleProjectReference,
        EvidenceRule.RequestMarker => RuleRequestMarker,
        EvidenceRule.HandlerMarker => RuleHandlerMarker,
        EvidenceRule.ValidatorMarker => RuleValidatorMarker,
        EvidenceRule.BehaviorClosedBinding => RuleBehaviorClosedBinding,
        EvidenceRule.BehaviorOpenGenericRegistration => RuleBehaviorOpenGenericRegistration,
        EvidenceRule.NotificationMarker => RuleNotificationMarker,
        EvidenceRule.NotificationHandlerMarker => RuleNotificationHandlerMarker,
        EvidenceRule.DbContextInheritance => RuleDbContextInheritance,
        EvidenceRule.EntityDiscovery => RuleEntityDiscovery,
        EvidenceRule.EndpointMapping => RuleEndpointMapping,
        EvidenceRule.DispatchCallSite => RuleDispatchCallSite,
        EvidenceRule.PublishCallSite => RulePublishCallSite,
        EvidenceRule.EndpointDispatch => RuleEndpointDispatch,
        EvidenceRule.DirectDbContextReference => RuleDirectDbContextReference,
        EvidenceRule.ResolvedDbContextDependency => RuleResolvedDbContextDependency,
        EvidenceRule.ServiceDeclarationCandidate => RuleServiceDeclarationCandidate,
        EvidenceRule.ServiceRegistrationBinding => RuleServiceRegistrationBinding,
        EvidenceRule.ServiceConfirmedBinding => RuleServiceConfirmedBinding,
        EvidenceRule.InjectedMemberInvocation => RuleInjectedMemberInvocation,
        EvidenceRule.DeepSliceDataAccess => RuleDeepSliceDataAccess,
        EvidenceRule.EntityNavigation => RuleEntityNavigation,
        EvidenceRule.EntityAnnotation => RuleEntityAnnotation,
        EvidenceRule.EntityFluentConfiguration => RuleEntityFluentConfiguration,
        _ => JsonEncodedText.Encode(JsonNamingPolicy.CamelCase.ConvertName(rule.ToString())),
    };
}
