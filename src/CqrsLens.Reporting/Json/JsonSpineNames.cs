using System.Text;
using System.Text.Json;

namespace CqrsLens;

/// <summary>Precomputed camelCase property names and enum tokens for spine JSON serialization.</summary>
internal static class JsonSpineNames
{
    private static class Encoded
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
        internal static readonly JsonEncodedText DetailProp = JsonEncodedText.Encode("detail");

        // Location properties
        internal static readonly JsonEncodedText FileProp = JsonEncodedText.Encode("file");
        internal static readonly JsonEncodedText LineProp = JsonEncodedText.Encode("line");
        internal static readonly JsonEncodedText ColumnProp = JsonEncodedText.Encode("column");

        // RelationKind tokens
        internal static readonly JsonEncodedText RelReferences = JsonEncodedText.Encode("references");
        internal static readonly JsonEncodedText RelServes = JsonEncodedText.Encode("serves");
        internal static readonly JsonEncodedText RelValidates = JsonEncodedText.Encode("validates");
        internal static readonly JsonEncodedText RelWraps = JsonEncodedText.Encode("wraps");
        internal static readonly JsonEncodedText RelDispatches = JsonEncodedText.Encode("dispatches");
        internal static readonly JsonEncodedText RelPublishes = JsonEncodedText.Encode("publishes");
        internal static readonly JsonEncodedText RelHandlesNotification = JsonEncodedText.Encode("handlesNotification");
        internal static readonly JsonEncodedText RelUsesData = JsonEncodedText.Encode("usesData");
        internal static readonly JsonEncodedText RelUsesDataResolved = JsonEncodedText.Encode("usesDataResolved");
        internal static readonly JsonEncodedText RelResolvesTo = JsonEncodedText.Encode("resolvesTo");
        internal static readonly JsonEncodedText RelCalls = JsonEncodedText.Encode("calls");
        internal static readonly JsonEncodedText RelTouchesData = JsonEncodedText.Encode("touchesData");
        internal static readonly JsonEncodedText RelRelates = JsonEncodedText.Encode("relates");
        internal static readonly JsonEncodedText RelExposes = JsonEncodedText.Encode("exposes");

        // NodeReferenceKind tokens
        internal static readonly JsonEncodedText NodeProject = JsonEncodedText.Encode("project");
        internal static readonly JsonEncodedText NodeLayer = JsonEncodedText.Encode("layer");
        internal static readonly JsonEncodedText NodeComponent = JsonEncodedText.Encode("component");
        internal static readonly JsonEncodedText NodeEndpoint = JsonEncodedText.Encode("endpoint");
        internal static readonly JsonEncodedText NodeInterface = JsonEncodedText.Encode("interface");
        internal static readonly JsonEncodedText NodeMethod = JsonEncodedText.Encode("method");
        internal static readonly JsonEncodedText NodeMember = JsonEncodedText.Encode("member");
        internal static readonly JsonEncodedText NodeType = JsonEncodedText.Encode("type");
        internal static readonly JsonEncodedText NodeExternal = JsonEncodedText.Encode("external");
        internal static readonly JsonEncodedText NodeUnresolved = JsonEncodedText.Encode("unresolved");

        // EvidenceStrength tokens
        internal static readonly JsonEncodedText StrProven = JsonEncodedText.Encode("proven");
        internal static readonly JsonEncodedText StrResolved = JsonEncodedText.Encode("resolved");
        internal static readonly JsonEncodedText StrInferred = JsonEncodedText.Encode("inferred");

        // EvidenceOrigin tokens
        internal static readonly JsonEncodedText OrigDeclaration = JsonEncodedText.Encode("declaration");
        internal static readonly JsonEncodedText OrigRegistration = JsonEncodedText.Encode("registration");
        internal static readonly JsonEncodedText OrigCallSite = JsonEncodedText.Encode("callSite");
        internal static readonly JsonEncodedText OrigMappingOrAttribute = JsonEncodedText.Encode("mappingOrAttribute");
        internal static readonly JsonEncodedText OrigProjectFile = JsonEncodedText.Encode("projectFile");
        internal static readonly JsonEncodedText OrigConfiguration = JsonEncodedText.Encode("configuration");

        // ResolutionSemantics tokens
        internal static readonly JsonEncodedText SemStaticCandidate = JsonEncodedText.Encode("staticCandidate");
        internal static readonly JsonEncodedText SemDeclaredBinding = JsonEncodedText.Encode("declaredBinding");
        internal static readonly JsonEncodedText SemConfirmed = JsonEncodedText.Encode("confirmed");

        // EvidenceRule tokens
        internal static readonly JsonEncodedText RuleProjectReference = JsonEncodedText.Encode("projectReference");
        internal static readonly JsonEncodedText RuleRequestMarker = JsonEncodedText.Encode("requestMarker");
        internal static readonly JsonEncodedText RuleHandlerMarker = JsonEncodedText.Encode("handlerMarker");
        internal static readonly JsonEncodedText RuleValidatorMarker = JsonEncodedText.Encode("validatorMarker");
        internal static readonly JsonEncodedText RuleBehaviorClosedBinding = JsonEncodedText.Encode("behaviorClosedBinding");
        internal static readonly JsonEncodedText RuleBehaviorOpenGenericRegistration = JsonEncodedText.Encode("behaviorOpenGenericRegistration");
        internal static readonly JsonEncodedText RuleNotificationMarker = JsonEncodedText.Encode("notificationMarker");
        internal static readonly JsonEncodedText RuleNotificationHandlerMarker = JsonEncodedText.Encode("notificationHandlerMarker");
        internal static readonly JsonEncodedText RuleDbContextInheritance = JsonEncodedText.Encode("dbContextInheritance");
        internal static readonly JsonEncodedText RuleEntityDiscovery = JsonEncodedText.Encode("entityDiscovery");
        internal static readonly JsonEncodedText RuleEndpointMapping = JsonEncodedText.Encode("endpointMapping");
        internal static readonly JsonEncodedText RuleDispatchCallSite = JsonEncodedText.Encode("dispatchCallSite");
        internal static readonly JsonEncodedText RulePublishCallSite = JsonEncodedText.Encode("publishCallSite");
        internal static readonly JsonEncodedText RuleEndpointDispatch = JsonEncodedText.Encode("endpointDispatch");
        internal static readonly JsonEncodedText RuleDirectDbContextReference = JsonEncodedText.Encode("directDbContextReference");
        internal static readonly JsonEncodedText RuleResolvedDbContextDependency = JsonEncodedText.Encode("resolvedDbContextDependency");
        internal static readonly JsonEncodedText RuleServiceDeclarationCandidate = JsonEncodedText.Encode("serviceDeclarationCandidate");
        internal static readonly JsonEncodedText RuleServiceRegistrationBinding = JsonEncodedText.Encode("serviceRegistrationBinding");
        internal static readonly JsonEncodedText RuleServiceConfirmedBinding = JsonEncodedText.Encode("serviceConfirmedBinding");
        internal static readonly JsonEncodedText RuleInjectedMemberInvocation = JsonEncodedText.Encode("injectedMemberInvocation");
        internal static readonly JsonEncodedText RuleDeepSliceDataAccess = JsonEncodedText.Encode("deepSliceDataAccess");
        internal static readonly JsonEncodedText RuleEntityNavigation = JsonEncodedText.Encode("entityNavigation");
        internal static readonly JsonEncodedText RuleEntityAnnotation = JsonEncodedText.Encode("entityAnnotation");
        internal static readonly JsonEncodedText RuleEntityFluentConfiguration = JsonEncodedText.Encode("entityFluentConfiguration");
        internal static readonly JsonEncodedText RuleControllerDiscovery = JsonEncodedText.Encode("controllerDiscovery");
        internal static readonly JsonEncodedText RuleServiceDiscovery = JsonEncodedText.Encode("serviceDiscovery");
        internal static readonly JsonEncodedText RuleRepositoryDiscovery = JsonEncodedText.Encode("repositoryDiscovery");
        internal static readonly JsonEncodedText RuleControllerEndpointExposure = JsonEncodedText.Encode("controllerEndpointExposure");
        internal static readonly JsonEncodedText RuleComponentMemberInvocation = JsonEncodedText.Encode("componentMemberInvocation");
        internal static readonly JsonEncodedText RuleRepositoryDataAccess = JsonEncodedText.Encode("repositoryDataAccess");
        internal static readonly JsonEncodedText RuleEventPublishCallSite = JsonEncodedText.Encode("eventPublishCallSite");
        internal static readonly JsonEncodedText RuleConventionRegistration = JsonEncodedText.Encode("conventionRegistration");
        internal static readonly JsonEncodedText RuleServiceLocatorCall = JsonEncodedText.Encode("serviceLocatorCall");
        internal static readonly JsonEncodedText RuleReflectionDiscovery = JsonEncodedText.Encode("reflectionDiscovery");
        internal static readonly JsonEncodedText RuleHttpClientRegistration = JsonEncodedText.Encode("httpClientRegistration");
    }

    internal static JsonEncodedText KindProp => Encoded.KindProp;
    internal static JsonEncodedText FromProp => Encoded.FromProp;
    internal static JsonEncodedText ToProp => Encoded.ToProp;
    internal static JsonEncodedText ViaProp => Encoded.ViaProp;
    internal static JsonEncodedText MethodProp => Encoded.MethodProp;
    internal static JsonEncodedText StrengthProp => Encoded.StrengthProp;
    internal static JsonEncodedText OccurrenceCountProp => Encoded.OccurrenceCountProp;
    internal static JsonEncodedText OccurrencesProp => Encoded.OccurrencesProp;

    internal static JsonEncodedText KeyProp => Encoded.KeyProp;
    internal static JsonEncodedText NameProp => Encoded.NameProp;
    internal static JsonEncodedText ComponentIdProp => Encoded.ComponentIdProp;
    internal static JsonEncodedText EndpointIdProp => Encoded.EndpointIdProp;
    internal static JsonEncodedText ProjectIdProp => Encoded.ProjectIdProp;
    internal static JsonEncodedText SymbolIdProp => Encoded.SymbolIdProp;
    internal static JsonEncodedText MemberProp => Encoded.MemberProp;
    internal static JsonEncodedText ReasonProp => Encoded.ReasonProp;

    internal static JsonEncodedText RuleProp => Encoded.RuleProp;
    internal static JsonEncodedText OriginProp => Encoded.OriginProp;
    internal static JsonEncodedText LocationProp => Encoded.LocationProp;
    internal static JsonEncodedText LocationOmittedReasonProp => Encoded.LocationOmittedReasonProp;
    internal static JsonEncodedText ResolutionSemanticsProp => Encoded.ResolutionSemanticsProp;
    internal static JsonEncodedText ScopeProjectProp => Encoded.ScopeProjectProp;
    internal static JsonEncodedText TestProjectProp => Encoded.TestProjectProp;
    internal static JsonEncodedText DetailProp => Encoded.DetailProp;

    internal static JsonEncodedText FileProp => Encoded.FileProp;
    internal static JsonEncodedText LineProp => Encoded.LineProp;
    internal static JsonEncodedText ColumnProp => Encoded.ColumnProp;

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
        RelationKind.Exposes => "exposes",
        _ => JsonNamingPolicy.CamelCase.ConvertName(kind.ToString()),
    };

    internal static JsonEncodedText RelationKindEncoded(RelationKind kind) => kind switch
    {
        RelationKind.References => Encoded.RelReferences,
        RelationKind.Serves => Encoded.RelServes,
        RelationKind.Validates => Encoded.RelValidates,
        RelationKind.Wraps => Encoded.RelWraps,
        RelationKind.Dispatches => Encoded.RelDispatches,
        RelationKind.Publishes => Encoded.RelPublishes,
        RelationKind.HandlesNotification => Encoded.RelHandlesNotification,
        RelationKind.UsesData => Encoded.RelUsesData,
        RelationKind.UsesDataResolved => Encoded.RelUsesDataResolved,
        RelationKind.ResolvesTo => Encoded.RelResolvesTo,
        RelationKind.Calls => Encoded.RelCalls,
        RelationKind.TouchesData => Encoded.RelTouchesData,
        RelationKind.Relates => Encoded.RelRelates,
        RelationKind.Exposes => Encoded.RelExposes,
        _ => JsonEncodedText.Encode(JsonNamingPolicy.CamelCase.ConvertName(kind.ToString())),
    };

    internal static ReadOnlySpan<byte> RelationKindUtf8(RelationKind kind) => kind switch
    {
        RelationKind.References => "references"u8,
        RelationKind.Serves => "serves"u8,
        RelationKind.Validates => "validates"u8,
        RelationKind.Wraps => "wraps"u8,
        RelationKind.Dispatches => "dispatches"u8,
        RelationKind.Publishes => "publishes"u8,
        RelationKind.HandlesNotification => "handlesNotification"u8,
        RelationKind.UsesData => "usesData"u8,
        RelationKind.UsesDataResolved => "usesDataResolved"u8,
        RelationKind.ResolvesTo => "resolvesTo"u8,
        RelationKind.Calls => "calls"u8,
        RelationKind.TouchesData => "touchesData"u8,
        RelationKind.Relates => "relates"u8,
        RelationKind.Exposes => "exposes"u8,
        _ => Encoding.UTF8.GetBytes(JsonNamingPolicy.CamelCase.ConvertName(kind.ToString())),
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
        NodeReferenceKind.Project => Encoded.NodeProject,
        NodeReferenceKind.Layer => Encoded.NodeLayer,
        NodeReferenceKind.Component => Encoded.NodeComponent,
        NodeReferenceKind.Endpoint => Encoded.NodeEndpoint,
        NodeReferenceKind.Interface => Encoded.NodeInterface,
        NodeReferenceKind.Method => Encoded.NodeMethod,
        NodeReferenceKind.Member => Encoded.NodeMember,
        NodeReferenceKind.Type => Encoded.NodeType,
        NodeReferenceKind.External => Encoded.NodeExternal,
        NodeReferenceKind.Unresolved => Encoded.NodeUnresolved,
        _ => JsonEncodedText.Encode(JsonNamingPolicy.CamelCase.ConvertName(kind.ToString())),
    };

    internal static ReadOnlySpan<byte> NodeReferenceKindUtf8(NodeReferenceKind kind) => kind switch
    {
        NodeReferenceKind.Project => "project"u8,
        NodeReferenceKind.Layer => "layer"u8,
        NodeReferenceKind.Component => "component"u8,
        NodeReferenceKind.Endpoint => "endpoint"u8,
        NodeReferenceKind.Interface => "interface"u8,
        NodeReferenceKind.Method => "method"u8,
        NodeReferenceKind.Member => "member"u8,
        NodeReferenceKind.Type => "type"u8,
        NodeReferenceKind.External => "external"u8,
        NodeReferenceKind.Unresolved => "unresolved"u8,
        _ => Encoding.UTF8.GetBytes(JsonNamingPolicy.CamelCase.ConvertName(kind.ToString())),
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
        EvidenceStrength.Proven => Encoded.StrProven,
        EvidenceStrength.Resolved => Encoded.StrResolved,
        EvidenceStrength.Inferred => Encoded.StrInferred,
        _ => JsonEncodedText.Encode(JsonNamingPolicy.CamelCase.ConvertName(strength.ToString())),
    };

    internal static ReadOnlySpan<byte> EvidenceStrengthUtf8(EvidenceStrength strength) => strength switch
    {
        EvidenceStrength.Proven => "proven"u8,
        EvidenceStrength.Resolved => "resolved"u8,
        EvidenceStrength.Inferred => "inferred"u8,
        _ => Encoding.UTF8.GetBytes(JsonNamingPolicy.CamelCase.ConvertName(strength.ToString())),
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
        EvidenceOrigin.Declaration => Encoded.OrigDeclaration,
        EvidenceOrigin.Registration => Encoded.OrigRegistration,
        EvidenceOrigin.CallSite => Encoded.OrigCallSite,
        EvidenceOrigin.MappingOrAttribute => Encoded.OrigMappingOrAttribute,
        EvidenceOrigin.ProjectFile => Encoded.OrigProjectFile,
        EvidenceOrigin.Configuration => Encoded.OrigConfiguration,
        _ => JsonEncodedText.Encode(JsonNamingPolicy.CamelCase.ConvertName(origin.ToString())),
    };

    internal static ReadOnlySpan<byte> EvidenceOriginUtf8(EvidenceOrigin origin) => origin switch
    {
        EvidenceOrigin.Declaration => "declaration"u8,
        EvidenceOrigin.Registration => "registration"u8,
        EvidenceOrigin.CallSite => "callSite"u8,
        EvidenceOrigin.MappingOrAttribute => "mappingOrAttribute"u8,
        EvidenceOrigin.ProjectFile => "projectFile"u8,
        EvidenceOrigin.Configuration => "configuration"u8,
        _ => Encoding.UTF8.GetBytes(JsonNamingPolicy.CamelCase.ConvertName(origin.ToString())),
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
        ResolutionSemantics.StaticCandidate => Encoded.SemStaticCandidate,
        ResolutionSemantics.DeclaredBinding => Encoded.SemDeclaredBinding,
        ResolutionSemantics.Confirmed => Encoded.SemConfirmed,
        _ => JsonEncodedText.Encode(JsonNamingPolicy.CamelCase.ConvertName(semantics.ToString())),
    };

    internal static ReadOnlySpan<byte> ResolutionSemanticsUtf8(ResolutionSemantics semantics) => semantics switch
    {
        ResolutionSemantics.StaticCandidate => "staticCandidate"u8,
        ResolutionSemantics.DeclaredBinding => "declaredBinding"u8,
        ResolutionSemantics.Confirmed => "confirmed"u8,
        _ => Encoding.UTF8.GetBytes(JsonNamingPolicy.CamelCase.ConvertName(semantics.ToString())),
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
        EvidenceRule.ControllerDiscovery => "controllerDiscovery",
        EvidenceRule.ServiceDiscovery => "serviceDiscovery",
        EvidenceRule.RepositoryDiscovery => "repositoryDiscovery",
        EvidenceRule.ControllerEndpointExposure => "controllerEndpointExposure",
        EvidenceRule.ComponentMemberInvocation => "componentMemberInvocation",
        EvidenceRule.RepositoryDataAccess => "repositoryDataAccess",
        EvidenceRule.EventPublishCallSite => "eventPublishCallSite",
        EvidenceRule.ConventionRegistration => "conventionRegistration",
        EvidenceRule.ServiceLocatorCall => "serviceLocatorCall",
        EvidenceRule.ReflectionDiscovery => "reflectionDiscovery",
        EvidenceRule.HttpClientRegistration => "httpClientRegistration",
        _ => JsonNamingPolicy.CamelCase.ConvertName(rule.ToString()),
    };

    internal static JsonEncodedText EvidenceRuleEncoded(EvidenceRule rule) => rule switch
    {
        EvidenceRule.ProjectReference => Encoded.RuleProjectReference,
        EvidenceRule.RequestMarker => Encoded.RuleRequestMarker,
        EvidenceRule.HandlerMarker => Encoded.RuleHandlerMarker,
        EvidenceRule.ValidatorMarker => Encoded.RuleValidatorMarker,
        EvidenceRule.BehaviorClosedBinding => Encoded.RuleBehaviorClosedBinding,
        EvidenceRule.BehaviorOpenGenericRegistration => Encoded.RuleBehaviorOpenGenericRegistration,
        EvidenceRule.NotificationMarker => Encoded.RuleNotificationMarker,
        EvidenceRule.NotificationHandlerMarker => Encoded.RuleNotificationHandlerMarker,
        EvidenceRule.DbContextInheritance => Encoded.RuleDbContextInheritance,
        EvidenceRule.EntityDiscovery => Encoded.RuleEntityDiscovery,
        EvidenceRule.EndpointMapping => Encoded.RuleEndpointMapping,
        EvidenceRule.DispatchCallSite => Encoded.RuleDispatchCallSite,
        EvidenceRule.PublishCallSite => Encoded.RulePublishCallSite,
        EvidenceRule.EndpointDispatch => Encoded.RuleEndpointDispatch,
        EvidenceRule.DirectDbContextReference => Encoded.RuleDirectDbContextReference,
        EvidenceRule.ResolvedDbContextDependency => Encoded.RuleResolvedDbContextDependency,
        EvidenceRule.ServiceDeclarationCandidate => Encoded.RuleServiceDeclarationCandidate,
        EvidenceRule.ServiceRegistrationBinding => Encoded.RuleServiceRegistrationBinding,
        EvidenceRule.ServiceConfirmedBinding => Encoded.RuleServiceConfirmedBinding,
        EvidenceRule.InjectedMemberInvocation => Encoded.RuleInjectedMemberInvocation,
        EvidenceRule.DeepSliceDataAccess => Encoded.RuleDeepSliceDataAccess,
        EvidenceRule.EntityNavigation => Encoded.RuleEntityNavigation,
        EvidenceRule.EntityAnnotation => Encoded.RuleEntityAnnotation,
        EvidenceRule.EntityFluentConfiguration => Encoded.RuleEntityFluentConfiguration,
        EvidenceRule.ControllerDiscovery => Encoded.RuleControllerDiscovery,
        EvidenceRule.ServiceDiscovery => Encoded.RuleServiceDiscovery,
        EvidenceRule.RepositoryDiscovery => Encoded.RuleRepositoryDiscovery,
        EvidenceRule.ControllerEndpointExposure => Encoded.RuleControllerEndpointExposure,
        EvidenceRule.ComponentMemberInvocation => Encoded.RuleComponentMemberInvocation,
        EvidenceRule.RepositoryDataAccess => Encoded.RuleRepositoryDataAccess,
        EvidenceRule.EventPublishCallSite => Encoded.RuleEventPublishCallSite,
        EvidenceRule.ConventionRegistration => Encoded.RuleConventionRegistration,
        EvidenceRule.ServiceLocatorCall => Encoded.RuleServiceLocatorCall,
        EvidenceRule.ReflectionDiscovery => Encoded.RuleReflectionDiscovery,
        EvidenceRule.HttpClientRegistration => Encoded.RuleHttpClientRegistration,
        _ => JsonEncodedText.Encode(JsonNamingPolicy.CamelCase.ConvertName(rule.ToString())),
    };

    internal static ReadOnlySpan<byte> EvidenceRuleUtf8(EvidenceRule rule) => rule switch
    {
        EvidenceRule.ProjectReference => "projectReference"u8,
        EvidenceRule.RequestMarker => "requestMarker"u8,
        EvidenceRule.HandlerMarker => "handlerMarker"u8,
        EvidenceRule.ValidatorMarker => "validatorMarker"u8,
        EvidenceRule.BehaviorClosedBinding => "behaviorClosedBinding"u8,
        EvidenceRule.BehaviorOpenGenericRegistration => "behaviorOpenGenericRegistration"u8,
        EvidenceRule.NotificationMarker => "notificationMarker"u8,
        EvidenceRule.NotificationHandlerMarker => "notificationHandlerMarker"u8,
        EvidenceRule.DbContextInheritance => "dbContextInheritance"u8,
        EvidenceRule.EntityDiscovery => "entityDiscovery"u8,
        EvidenceRule.EndpointMapping => "endpointMapping"u8,
        EvidenceRule.DispatchCallSite => "dispatchCallSite"u8,
        EvidenceRule.PublishCallSite => "publishCallSite"u8,
        EvidenceRule.EndpointDispatch => "endpointDispatch"u8,
        EvidenceRule.DirectDbContextReference => "directDbContextReference"u8,
        EvidenceRule.ResolvedDbContextDependency => "resolvedDbContextDependency"u8,
        EvidenceRule.ServiceDeclarationCandidate => "serviceDeclarationCandidate"u8,
        EvidenceRule.ServiceRegistrationBinding => "serviceRegistrationBinding"u8,
        EvidenceRule.ServiceConfirmedBinding => "serviceConfirmedBinding"u8,
        EvidenceRule.InjectedMemberInvocation => "injectedMemberInvocation"u8,
        EvidenceRule.DeepSliceDataAccess => "deepSliceDataAccess"u8,
        EvidenceRule.EntityNavigation => "entityNavigation"u8,
        EvidenceRule.EntityAnnotation => "entityAnnotation"u8,
        EvidenceRule.EntityFluentConfiguration => "entityFluentConfiguration"u8,
        EvidenceRule.ControllerDiscovery => "controllerDiscovery"u8,
        EvidenceRule.ServiceDiscovery => "serviceDiscovery"u8,
        EvidenceRule.RepositoryDiscovery => "repositoryDiscovery"u8,
        EvidenceRule.ControllerEndpointExposure => "controllerEndpointExposure"u8,
        EvidenceRule.ComponentMemberInvocation => "componentMemberInvocation"u8,
        EvidenceRule.RepositoryDataAccess => "repositoryDataAccess"u8,
        EvidenceRule.EventPublishCallSite => "eventPublishCallSite"u8,
        EvidenceRule.ConventionRegistration => "conventionRegistration"u8,
        EvidenceRule.ServiceLocatorCall => "serviceLocatorCall"u8,
        EvidenceRule.ReflectionDiscovery => "reflectionDiscovery"u8,
        EvidenceRule.HttpClientRegistration => "httpClientRegistration"u8,
        _ => Encoding.UTF8.GetBytes(JsonNamingPolicy.CamelCase.ConvertName(rule.ToString())),
    };

    internal static string ComponentRoleName(ComponentRole role) => role switch
    {
        ComponentRole.Request => "request",
        ComponentRole.Handler => "handler",
        ComponentRole.Notification => "notification",
        ComponentRole.NotificationHandler => "notificationHandler",
        ComponentRole.Validator => "validator",
        ComponentRole.Behavior => "behavior",
        ComponentRole.DbContext => "dbContext",
        ComponentRole.Entity => "entity",
        ComponentRole.Endpoint => "endpoint",
        ComponentRole.Controller => "controller",
        ComponentRole.Service => "service",
        ComponentRole.Repository => "repository",
        _ => JsonNamingPolicy.CamelCase.ConvertName(role.ToString()),
    };

    internal static ReadOnlySpan<byte> ComponentRoleUtf8(ComponentRole role) => role switch
    {
        ComponentRole.Request => "request"u8,
        ComponentRole.Handler => "handler"u8,
        ComponentRole.Notification => "notification"u8,
        ComponentRole.NotificationHandler => "notificationHandler"u8,
        ComponentRole.Validator => "validator"u8,
        ComponentRole.Behavior => "behavior"u8,
        ComponentRole.DbContext => "dbContext"u8,
        ComponentRole.Entity => "entity"u8,
        ComponentRole.Endpoint => "endpoint"u8,
        ComponentRole.Controller => "controller"u8,
        ComponentRole.Service => "service"u8,
        ComponentRole.Repository => "repository"u8,
        _ => Encoding.UTF8.GetBytes(JsonNamingPolicy.CamelCase.ConvertName(role.ToString())),
    };

    internal static string UnprovenItemKindName(UnprovenItemKind kind) => kind switch
    {
        UnprovenItemKind.AmbiguousLink => "ambiguousLink",
        UnprovenItemKind.UnresolvedContract => "unresolvedContract",
        UnprovenItemKind.UnresolvedEntity => "unresolvedEntity",
        UnprovenItemKind.UnresolvedNavigation => "unresolvedNavigation",
        UnprovenItemKind.UnresolvedConfiguration => "unresolvedConfiguration",
        UnprovenItemKind.ConventionRegistration => "conventionRegistration",
        UnprovenItemKind.ServiceLocator => "serviceLocator",
        UnprovenItemKind.DynamicReflection => "dynamicReflection",
        UnprovenItemKind.UnresolvedEventConsumer => "unresolvedEventConsumer",
        UnprovenItemKind.UnimplementedInterface => "unimplementedInterface",
        _ => JsonNamingPolicy.CamelCase.ConvertName(kind.ToString()),
    };

    internal static ReadOnlySpan<byte> UnprovenItemKindUtf8(UnprovenItemKind kind) => kind switch
    {
        UnprovenItemKind.AmbiguousLink => "ambiguousLink"u8,
        UnprovenItemKind.UnresolvedContract => "unresolvedContract"u8,
        UnprovenItemKind.UnresolvedEntity => "unresolvedEntity"u8,
        UnprovenItemKind.UnresolvedNavigation => "unresolvedNavigation"u8,
        UnprovenItemKind.UnresolvedConfiguration => "unresolvedConfiguration"u8,
        UnprovenItemKind.ConventionRegistration => "conventionRegistration"u8,
        UnprovenItemKind.ServiceLocator => "serviceLocator"u8,
        UnprovenItemKind.DynamicReflection => "dynamicReflection"u8,
        UnprovenItemKind.UnresolvedEventConsumer => "unresolvedEventConsumer"u8,
        UnprovenItemKind.UnimplementedInterface => "unimplementedInterface"u8,
        _ => Encoding.UTF8.GetBytes(JsonNamingPolicy.CamelCase.ConvertName(kind.ToString())),
    };

    internal static string UnestablishedBehaviorReasonName(UnestablishedBehaviorReason reason) => reason switch
    {
        UnestablishedBehaviorReason.NoRegistrationSite => "noRegistrationSite",
        UnestablishedBehaviorReason.AssemblyScanningOrExternal => "assemblyScanningOrExternal",
        UnestablishedBehaviorReason.GenericConstraintsNotEvaluated => "genericConstraintsNotEvaluated",
        UnestablishedBehaviorReason.ClosedFormUnknown => "closedFormUnknown",
        _ => JsonNamingPolicy.CamelCase.ConvertName(reason.ToString()),
    };

    internal static ReadOnlySpan<byte> UnestablishedBehaviorReasonUtf8(UnestablishedBehaviorReason reason) => reason switch
    {
        UnestablishedBehaviorReason.NoRegistrationSite => "noRegistrationSite"u8,
        UnestablishedBehaviorReason.AssemblyScanningOrExternal => "assemblyScanningOrExternal"u8,
        UnestablishedBehaviorReason.GenericConstraintsNotEvaluated => "genericConstraintsNotEvaluated"u8,
        UnestablishedBehaviorReason.ClosedFormUnknown => "closedFormUnknown"u8,
        _ => Encoding.UTF8.GetBytes(JsonNamingPolicy.CamelCase.ConvertName(reason.ToString())),
    };
}
