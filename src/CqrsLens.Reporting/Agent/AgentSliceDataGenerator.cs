using System.Text.Json.Nodes;
using static CqrsLens.AgentReportFormatter;
using static CqrsLens.AgentReportLimits;
using static CqrsLens.AgentReportLookups;
using static CqrsLens.AgentSliceMatcher;

namespace CqrsLens;

/// <summary>
/// Returns a matched vertical slice as compact typed data instead of rendered prose. This is the
/// structured counterpart of <see cref="AgentSliceWriter"/>: the request(s), their handlers,
/// validators, the pipeline behaviors in registered order, notifications and their handlers, and
/// the model's endpoint and DbContext flow edges. Every value is read from the scan model; a value
/// the model does not hold is omitted rather than completed by convention.
/// </summary>
internal static class AgentSliceDataGenerator
{
    internal static string Generate(AnalysisResult result, string? sliceFilter, int maxSlices)
    {
        ArgumentNullException.ThrowIfNull(result);

        var filter = string.IsNullOrWhiteSpace(sliceFilter) ? null : sliceFilter.Trim();
        var rootName = new DirectoryInfo(result.RootPath).Name;

        var (handlersByFull, handlersBySimple) = BuildLookups(result.Handlers, h => h.FullName, h => h.Name);
        var (validatorsByFull, validatorsBySimple) = BuildLookups(result.Validators, v => v.FullName, v => v.Name);
        var (notificationsByFull, notificationsBySimple) = BuildLookups(result.Notifications, n => n.FullName, n => n.Name);
        var (notifHandlersByFull, notifHandlersBySimple) = BuildLookups(result.NotificationHandlers, h => h.FullName, h => h.Name);

        var endpointsByRequest = result.Endpoints
            .GroupBy(e => e.TargetRequestName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var duplicateRequestNames = new HashSet<string>(
            result.Requests
                .GroupBy(r => r.Name, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key),
            StringComparer.Ordinal);

        // The same matched requests, in the same rank order, the rendered text form would show.
        var matchedCount = 0;
        var matched = new List<CqrsRequest>();
        var genericKindFilter = false;

        if (filter is null)
        {
            matched.AddRange(result.Requests);
            matchedCount = matched.Count;
        }
        else
        {
            var hasExactName = result.Requests.Any(r =>
                string.Equals(r.Name, filter, StringComparison.OrdinalIgnoreCase));

            if (GenericKindFilters.Contains(filter) && !hasExactName)
            {
                genericKindFilter = true;
            }
            else
            {
                var ranked = result.Requests
                    .Select(r => (Request: r, Score: ScoreSlice(
                        r, filter, handlersByFull, handlersBySimple,
                        validatorsByFull, validatorsBySimple, endpointsByRequest)))
                    .Where(hit => hit.Score > 0)
                    .OrderByDescending(hit => hit.Score)
                    .ThenBy(hit => hit.Request.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(hit => hit.Request.FullName, StringComparer.Ordinal)
                    .ToList();

                matchedCount = ranked.Count;
                var limit = maxSlices > 0 ? Math.Min(maxSlices, MaxSliceLimit) : DefaultSliceLimit;
                matched.AddRange(ranked.Take(limit).Select(hit => hit.Request));
            }
        }

        var root = new JsonObject
        {
            ["root"] = rootName,
            ["query"] = filter,
            ["totalMatched"] = matchedCount,
            ["shown"] = matched.Count,
        };

        if (genericKindFilter)
        {
            root["note"] =
                $"`{filter}` names a CQRS kind, not a slice. Search by a domain noun or exact request name.";
        }

        root["behaviors"] = BuildBehaviors(result.Behaviors);

        var slices = new JsonArray();
        foreach (var r in matched)
        {
            slices.Add(BuildSlice(
                r,
                result,
                endpointsByRequest,
                handlersByFull,
                handlersBySimple,
                validatorsByFull,
                validatorsBySimple,
                notificationsByFull,
                notificationsBySimple,
                notifHandlersByFull,
                notifHandlersBySimple,
                allowSimpleNameEndpointFallback: !duplicateRequestNames.Contains(r.Name)));
        }

        root["slices"] = slices;
        return root.ToJsonString();
    }

    private static JsonObject BuildBehaviors(IReadOnlyList<BehaviorInfo> behaviors)
    {
        var registered = new JsonArray();
        foreach (var group in BehaviorOrdering.Registered(behaviors))
            foreach (var behavior in group.Behaviors)
                registered.Add(BehaviorNode(behavior, orderDeterminable: true));

        var unregistered = new JsonArray();
        foreach (var behavior in BehaviorOrdering.Unregistered(behaviors))
            unregistered.Add(BehaviorNode(behavior, orderDeterminable: false));

        // The registered-order / not-guaranteed-execution-order label is the model's own wording.
        return new JsonObject
        {
            ["label"] = BehaviorPresentationText.RegisteredOrderNote,
            ["registered"] = registered,
            ["unregistered"] = unregistered,
            ["unregisteredLabel"] = BehaviorPresentationText.UnregisteredHeading,
        };
    }

    private static JsonObject BehaviorNode(BehaviorInfo behavior, bool orderDeterminable)
    {
        var node = new JsonObject
        {
            ["name"] = behavior.Name,
            ["orderDeterminable"] = orderDeterminable,
        };
        if (!string.IsNullOrEmpty(behavior.ProjectName))
            node["project"] = behavior.ProjectName;
        if (behavior.Location is not null)
            node["location"] = $"{behavior.Location.File}:{behavior.Location.Line}";
        if (behavior.RegistrationOrder is { } order)
            node["registeredOrder"] = order;
        if (!string.IsNullOrEmpty(behavior.RegistrationSite))
            node["registrationSite"] = behavior.RegistrationSite;
        return node;
    }

    private static JsonObject BuildSlice(
        CqrsRequest request,
        AnalysisResult result,
        IReadOnlyDictionary<string, List<EndpointInfo>> endpointsByRequest,
        IReadOnlyDictionary<string, HandlerInfo> handlersByFull,
        IReadOnlyDictionary<string, HandlerInfo> handlersBySimple,
        IReadOnlyDictionary<string, ValidatorInfo> validatorsByFull,
        IReadOnlyDictionary<string, ValidatorInfo> validatorsBySimple,
        IReadOnlyDictionary<string, CqrsNotification> notificationsByFull,
        IReadOnlyDictionary<string, CqrsNotification> notificationsBySimple,
        IReadOnlyDictionary<string, NotificationHandlerInfo> notifHandlersByFull,
        IReadOnlyDictionary<string, NotificationHandlerInfo> notifHandlersBySimple,
        bool allowSimpleNameEndpointFallback)
    {
        var slice = new JsonObject
        {
            ["request"] = RequestNode(request),
            ["validators"] = ValidatorNodes(request, validatorsByFull, validatorsBySimple),
            ["handlers"] = HandlerNodes(request, handlersByFull, handlersBySimple),
            ["notifications"] = NotificationNodes(
                request, handlersByFull, handlersBySimple, notificationsByFull, notificationsBySimple,
                notifHandlersByFull, notifHandlersBySimple),
            ["endpointEdges"] = EndpointEdgeNodes(request, result),
            ["dbContextEdges"] = DbContextEdgeNodes(request, result, handlersByFull, handlersBySimple),
            ["serviceCallEdges"] = ServiceCallEdgeNodes(
                request, result, handlersByFull, handlersBySimple,
                notificationsByFull, notificationsBySimple,
                notifHandlersByFull, notifHandlersBySimple),
        };

        var endpoints = ResolvedEndpointNodes(request, endpointsByRequest, allowSimpleNameEndpointFallback);
        if (endpoints.Count > 0)
            slice["endpoints"] = endpoints;
        if (request.Entrypoints.Count > 0)
            slice["entrypoints"] = StringArray(request.Entrypoints);

        return slice;
    }

    private static JsonObject RequestNode(CqrsRequest request)
    {
        var node = new JsonObject
        {
            ["name"] = request.Name,
            ["fullName"] = request.FullName,
            ["kind"] = request.Kind,
        };
        if (!string.IsNullOrEmpty(request.ResponseType))
            node["responseType"] = request.ResponseType;
        if (!string.IsNullOrEmpty(request.ProjectName))
            node["project"] = request.ProjectName;
        if (request.Location is not null)
            node["location"] = $"{request.Location.File}:{request.Location.Line}";
        if (!string.IsNullOrEmpty(request.ViaMarker))
            node["viaMarker"] = request.ViaMarker;
        if (request.Properties.Count > 0)
        {
            var payload = new JsonArray();
            foreach (var property in request.Properties)
                payload.Add(new JsonObject { ["name"] = property.Name, ["type"] = property.Type });
            node["payload"] = payload;
        }
        return node;
    }

    private static JsonArray ValidatorNodes(
        CqrsRequest request,
        IReadOnlyDictionary<string, ValidatorInfo> validatorsByFull,
        IReadOnlyDictionary<string, ValidatorInfo> validatorsBySimple)
    {
        var nodes = new JsonArray();
        foreach (var name in request.Validators)
        {
            var validator = Resolve(name, validatorsByFull, validatorsBySimple);
            var node = new JsonObject
            {
                ["name"] = validator?.Name ?? CSharpScanner.SimpleName(name),
                ["fullName"] = validator?.FullName ?? name,
            };
            if (!string.IsNullOrEmpty(validator?.ProjectName))
                node["project"] = validator!.ProjectName;
            if (validator?.Location is not null)
                node["location"] = $"{validator.Location.File}:{validator.Location.Line}";
            nodes.Add(node);
        }
        return nodes;
    }

    private static JsonArray HandlerNodes(
        CqrsRequest request,
        IReadOnlyDictionary<string, HandlerInfo> handlersByFull,
        IReadOnlyDictionary<string, HandlerInfo> handlersBySimple)
    {
        var nodes = new JsonArray();
        foreach (var name in request.Handlers)
        {
            var handler = Resolve(name, handlersByFull, handlersBySimple);
            var node = new JsonObject
            {
                ["name"] = handler?.Name ?? CSharpScanner.SimpleName(name),
                ["fullName"] = handler?.FullName ?? name,
            };
            if (!string.IsNullOrEmpty(handler?.ProjectName))
                node["project"] = handler!.ProjectName;
            if (handler?.Location is not null)
                node["location"] = $"{handler.Location.File}:{handler.Location.Line}";
            if (handler is not null)
            {
                if (handler.Dependencies.Count > 0)
                    node["dependencies"] = StringArray(handler.Dependencies);
                if (handler.TouchedEntities.Count > 0)
                    node["touchedDbSets"] = StringArray(handler.TouchedEntities);
                if (handler.PublishedNotifications.Count > 0)
                    node["publishedNotifications"] = StringArray(handler.PublishedNotifications);
                if (handler.DispatchedRequests.Count > 0)
                    node["dispatchedRequests"] = StringArray(handler.DispatchedRequests);
            }
            nodes.Add(node);
        }
        return nodes;
    }

    private static JsonArray NotificationNodes(
        CqrsRequest request,
        IReadOnlyDictionary<string, HandlerInfo> handlersByFull,
        IReadOnlyDictionary<string, HandlerInfo> handlersBySimple,
        IReadOnlyDictionary<string, CqrsNotification> notificationsByFull,
        IReadOnlyDictionary<string, CqrsNotification> notificationsBySimple,
        IReadOnlyDictionary<string, NotificationHandlerInfo> notifHandlersByFull,
        IReadOnlyDictionary<string, NotificationHandlerInfo> notifHandlersBySimple)
    {
        var names = new List<string>();
        void Add(string notification)
        {
            if (!names.Contains(notification, StringComparer.Ordinal))
                names.Add(notification);
        }

        foreach (var notification in request.PublishedNotifications)
            Add(notification);
        foreach (var handlerName in request.Handlers)
        {
            var handler = Resolve(handlerName, handlersByFull, handlersBySimple);
            if (handler is null)
                continue;
            foreach (var notification in handler.PublishedNotifications)
                Add(notification);
        }

        var nodes = new JsonArray();
        foreach (var name in names)
        {
            var notification = Resolve(name, notificationsByFull, notificationsBySimple);
            var node = new JsonObject
            {
                ["name"] = notification?.Name ?? CSharpScanner.SimpleName(name),
                ["fullName"] = notification?.FullName ?? name,
            };
            if (!string.IsNullOrEmpty(notification?.ProjectName))
                node["project"] = notification!.ProjectName;
            if (notification?.Location is not null)
                node["location"] = $"{notification.Location.File}:{notification.Location.Line}";
            if (!string.IsNullOrEmpty(notification?.ViaMarker))
                node["viaMarker"] = notification!.ViaMarker;

            var handlers = new JsonArray();
            var notificationHandlers = notification is { } resolved
                ? resolved.Handlers
                : (IEnumerable<string>)Array.Empty<string>();
            foreach (var handlerName in notificationHandlers)
            {
                var handler = Resolve(handlerName, notifHandlersByFull, notifHandlersBySimple);
                var handlerNode = new JsonObject
                {
                    ["name"] = handler?.Name ?? CSharpScanner.SimpleName(handlerName),
                    ["fullName"] = handler?.FullName ?? handlerName,
                };
                if (!string.IsNullOrEmpty(handler?.ProjectName))
                    handlerNode["project"] = handler!.ProjectName;
                if (handler?.Location is not null)
                    handlerNode["location"] = $"{handler.Location.File}:{handler.Location.Line}";
                if (handler is not null)
                {
                    if (handler.Dependencies.Count > 0)
                        handlerNode["dependencies"] = StringArray(handler.Dependencies);
                    if (handler.TouchedEntities.Count > 0)
                        handlerNode["touchedDbSets"] = StringArray(handler.TouchedEntities);
                    if (handler.PublishedNotifications.Count > 0)
                        handlerNode["publishedNotifications"] = StringArray(handler.PublishedNotifications);
                    if (handler.DispatchedRequests.Count > 0)
                        handlerNode["dispatchedRequests"] = StringArray(handler.DispatchedRequests);
                }
                handlers.Add(handlerNode);
            }
            node["handlers"] = handlers;
            nodes.Add(node);
        }
        return nodes;
    }

    private static JsonArray ResolvedEndpointNodes(
        CqrsRequest request,
        IReadOnlyDictionary<string, List<EndpointInfo>> endpointsByRequest,
        bool allowSimpleNameEndpointFallback)
    {
        var nodes = new JsonArray();
        if (!endpointsByRequest.TryGetValue(request.Name, out var endpoints))
            return nodes;

        foreach (var endpoint in endpoints.Where(e => EndpointTargetsRequest(e, request, allowSimpleNameEndpointFallback)))
        {
            var node = new JsonObject
            {
                ["name"] = endpoint.Name,
                ["label"] = endpoint.DisplayLabel,
                ["httpMethod"] = endpoint.HttpMethod,
                ["route"] = endpoint.Route,
                ["targetRequest"] = string.IsNullOrEmpty(endpoint.TargetRequestFullName)
                    ? endpoint.TargetRequestName
                    : endpoint.TargetRequestFullName,
                ["inferred"] = endpoint.IsInferred,
            };
            if (!string.IsNullOrEmpty(endpoint.ProjectName))
                node["project"] = endpoint.ProjectName;
            if (endpoint.Location is not null)
                node["location"] = $"{endpoint.Location.File}:{endpoint.Location.Line}";
            nodes.Add(node);
        }
        return nodes;
    }

    private static JsonArray EndpointEdgeNodes(CqrsRequest request, AnalysisResult result)
    {
        var nodes = new JsonArray();
        foreach (var edge in result.FlowEdges)
        {
            if (edge.Kind != FlowEdgeKind.EndpointToRequest)
                continue;
            if (!string.Equals(edge.To, request.FullName, StringComparison.Ordinal))
                continue;
            nodes.Add(FlowEdgeNode(edge));
        }
        return nodes;
    }

    private static JsonArray DbContextEdgeNodes(
        CqrsRequest request,
        AnalysisResult result,
        IReadOnlyDictionary<string, HandlerInfo> handlersByFull,
        IReadOnlyDictionary<string, HandlerInfo> handlersBySimple)
    {
        var handlerNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in request.Handlers)
        {
            handlerNames.Add(name);
            var handler = Resolve(name, handlersByFull, handlersBySimple);
            if (handler is not null)
                handlerNames.Add(handler.FullName);
        }

        var nodes = new JsonArray();
        foreach (var edge in result.FlowEdges)
        {
            if (edge.Kind != FlowEdgeKind.HandlerToDbContext)
                continue;
            if (!handlerNames.Contains(edge.From))
                continue;
            nodes.Add(FlowEdgeNode(edge));
        }
        return nodes;
    }

    /// <summary>
    /// Method-body service-call edges whose From is a member of this slice: one of the
    /// request's handlers, or a notification handler named under a published notification.
    /// A service target's own outgoing calls are not members, so no chain is expanded.
    /// </summary>
    private static JsonArray ServiceCallEdgeNodes(
        CqrsRequest request,
        AnalysisResult result,
        IReadOnlyDictionary<string, HandlerInfo> handlersByFull,
        IReadOnlyDictionary<string, HandlerInfo> handlersBySimple,
        IReadOnlyDictionary<string, CqrsNotification> notificationsByFull,
        IReadOnlyDictionary<string, CqrsNotification> notificationsBySimple,
        IReadOnlyDictionary<string, NotificationHandlerInfo> notifHandlersByFull,
        IReadOnlyDictionary<string, NotificationHandlerInfo> notifHandlersBySimple)
    {
        var members = new HashSet<string>(StringComparer.Ordinal);
        var notificationNames = new List<string>();

        void AddNotification(string name)
        {
            if (!notificationNames.Contains(name, StringComparer.Ordinal))
                notificationNames.Add(name);
        }

        foreach (var name in request.Handlers)
        {
            members.Add(name);
            var handler = Resolve(name, handlersByFull, handlersBySimple);
            if (handler is null)
                continue;
            members.Add(handler.FullName);
            foreach (var published in handler.PublishedNotifications)
                AddNotification(published);
        }

        foreach (var published in request.PublishedNotifications)
            AddNotification(published);

        foreach (var name in notificationNames)
        {
            var notification = Resolve(name, notificationsByFull, notificationsBySimple);
            if (notification is null)
                continue;
            foreach (var handlerName in notification.Handlers)
            {
                members.Add(handlerName);
                var handler = Resolve(handlerName, notifHandlersByFull, notifHandlersBySimple);
                if (handler is not null)
                    members.Add(handler.FullName);
            }
        }

        var nodes = new JsonArray();
        foreach (var edge in result.ServiceCallEdges)
        {
            if (!members.Contains(edge.From))
                continue;
            var node = new JsonObject
            {
                ["kind"] = "serviceCall",
                ["from"] = edge.From,
                ["via"] = edge.Via,
                ["method"] = edge.Method,
                ["to"] = edge.To,
            };
            if (!string.IsNullOrEmpty(edge.ProjectName))
                node["project"] = edge.ProjectName;
            if (edge.Location is not null)
                node["location"] = $"{edge.Location.File}:{edge.Location.Line}";
            nodes.Add(node);
        }
        return nodes;
    }

    private static JsonObject FlowEdgeNode(FlowEdge edge)
    {
        var node = new JsonObject
        {
            ["kind"] = FlowEdgeKindName(edge.Kind),
            ["from"] = edge.From,
            ["to"] = edge.To,
        };
        if (!string.IsNullOrEmpty(edge.ProjectName))
            node["project"] = edge.ProjectName;
        if (edge.Location is not null)
            node["location"] = $"{edge.Location.File}:{edge.Location.Line}";
        return node;
    }

    private static string FlowEdgeKindName(FlowEdgeKind kind) => kind switch
    {
        FlowEdgeKind.EndpointToRequest => "endpointToRequest",
        FlowEdgeKind.RequestToHandler => "requestToHandler",
        FlowEdgeKind.HandlerToRequest => "handlerToRequest",
        FlowEdgeKind.HandlerToNotification => "handlerToNotification",
        FlowEdgeKind.HandlerToDbContext => "handlerToDbContext",
        _ => "unknown",
    };

    private static JsonArray StringArray(IEnumerable<string> values)
    {
        var array = new JsonArray();
        foreach (var value in values)
            array.Add(value);
        return array;
    }
}
