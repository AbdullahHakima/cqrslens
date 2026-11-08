using System.Text;
using static CqrsLens.AgentReportFormatter;
using static CqrsLens.AgentReportLimits;
using static CqrsLens.AgentReportLookups;

namespace CqrsLens;

/// <summary>
/// Generates the request-impact trace for MCP consumers: what one request touches (its handler,
/// validators, the pipeline behaviors in registered order, the endpoints that dispatch it, the
/// notifications it publishes and their handlers, and the handler→DbContext edges the model
/// holds) and what in the model references it (reverse reach). Every fact is read from the scan
/// model; an absent link is stated as absent, never completed by convention, and an unknown
/// request name is a clean not-found answer rather than a guess.
/// </summary>
internal static class AgentRequestImpactGenerator
{
    internal static string Generate(
        AnalysisResult result,
        string requestFilter,
        bool containsFilter = false,
        int maxResults = 0)
    {
        ArgumentNullException.ThrowIfNull(result);
        var filter = (requestFilter ?? "").Trim();
        var rootName = new DirectoryInfo(result.RootPath).Name;
        var sb = new StringBuilder();

        sb.AppendLine($"# CqrsLens Request Impact Trace — `{filter}` (`{rootName}`)");
        sb.AppendLine();

        var matched = result.Requests
            .Where(r => Matches(r, filter, containsFilter))
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.FullName, StringComparer.Ordinal)
            .ToList();

        if (matched.Count == 0)
        {
            AppendNotFound(sb, result, filter, containsFilter);
            return sb.ToString();
        }

        var limit = maxResults > 0 ? Math.Min(maxResults, 50) : 25;
        var shown = matched.Take(limit).ToList();

        sb.AppendLine(matched.Count == shown.Count
            ? $"Matched {matched.Count} request(s) in the scanned model; tracing all of them."
            : $"Matched {matched.Count} request(s) in the scanned model; showing {shown.Count} (limit {limit}).");
        sb.AppendLine();

        var (handlersByFull, handlersBySimple) = BuildLookups(result.Handlers, h => h.FullName, h => h.Name);
        var (validatorsByFull, validatorsBySimple) = BuildLookups(result.Validators, v => v.FullName, v => v.Name);
        var (notificationsByFull, notificationsBySimple) =
            BuildLookups(result.Notifications, n => n.FullName, n => n.Name);
        var (notifHandlersByFull, notifHandlersBySimple) =
            BuildLookups(result.NotificationHandlers, h => h.FullName, h => h.Name);

        var duplicateNames = new HashSet<string>(
            result.Requests.GroupBy(r => r.Name, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key),
            StringComparer.Ordinal);

        foreach (var request in shown)
        {
            AppendRequestImpact(
                sb, result, request,
                handlersByFull, handlersBySimple,
                validatorsByFull, validatorsBySimple,
                notificationsByFull, notificationsBySimple,
                notifHandlersByFull, notifHandlersBySimple,
                allowSimpleNameEndpointFallback: !duplicateNames.Contains(request.Name));
        }

        if (matched.Count > shown.Count)
        {
            sb.AppendLine($"_+{matched.Count - shown.Count} more matching request(s) — narrow the name or raise `limit`._");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static bool Matches(CqrsRequest request, string filter, bool contains)
    {
        if (contains)
        {
            return request.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                   request.FullName.Contains(filter, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(request.Name, filter, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(request.FullName, filter, StringComparison.OrdinalIgnoreCase);
    }

    private static void AppendRequestImpact(
        StringBuilder sb,
        AnalysisResult result,
        CqrsRequest request,
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
        var response = string.IsNullOrEmpty(request.ResponseType) ? "void" : request.ResponseType;

        var handlers = request.Handlers
            .Select(name => Resolve(name, handlersByFull, handlersBySimple))
            .Where(h => h is not null)
            .Select(h => h!)
            .ToList();

        var endpoints = result.Endpoints
            .Where(e => EndpointTargetsRequest(e, request, allowSimpleNameEndpointFallback))
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Route, StringComparer.Ordinal)
            .ToList();

        var notifications = PublishedNotifications(
            request, handlers, request.PublishedNotifications,
            notificationsByFull, notificationsBySimple);

        var dbContextEdges = DbContextEdges(result, handlers);
        var references = ReverseReach(result, request, allowSimpleNameEndpointFallback);

        var registered = BehaviorOrdering.Registered(result.Behaviors);
        var unregistered = BehaviorOrdering.Unregistered(result.Behaviors);

        sb.AppendLine($"## [{request.Kind}] `{request.Name}` → `{response}`{FormatLoc(request.Location)}" +
                      ProjectSuffix(request.ProjectName));
        sb.AppendLine(
            $"- Handlers: {request.Handlers.Count} · Validators: {request.Validators.Count} · " +
            $"Published notifications: {notifications.Count} · Endpoints: {endpoints.Count} · " +
            $"DbContext edges: {dbContextEdges.Count} · Reverse reach: {references.Count}");
        sb.AppendLine();

        AppendHandlers(sb, request, handlers);
        AppendValidators(sb, request, validatorsByFull, validatorsBySimple);
        AppendBehaviors(sb, registered, unregistered);
        AppendEndpoints(sb, request, endpoints, result);
        AppendNotifications(sb, notifications, notifHandlersByFull, notifHandlersBySimple);
        AppendDbContextEdges(sb, dbContextEdges);
        AppendReverseReach(sb, references, result);
    }

    private static void AppendHandlers(StringBuilder sb, CqrsRequest request, IReadOnlyList<HandlerInfo> handlers)
    {
        sb.AppendLine($"### Handlers ({handlers.Count})");
        if (handlers.Count == 0)
        {
            sb.AppendLine(request.Handlers.Count == 0
                ? "The scan model links no handler to this request."
                : $"The scan model holds {request.Handlers.Count} unresolved handler name(s) for this request: " +
                  $"{JoinLimited(request.Handlers, MaxListItems, n => $"`{n}`")}.");
            sb.AppendLine();
            return;
        }

        foreach (var handler in handlers)
        {
            var deps = handler.Dependencies.Count > 0
                ? $" · Deps: {JoinLimited(handler.Dependencies, MaxListItems, d => $"`{d}`")}"
                : "";
            sb.AppendLine($"- `{handler.Name}`{FormatLoc(handler.Location)}{deps}");
            if (handler.TouchedEntities.Count > 0)
                sb.AppendLine($"  - DbSet touches: {JoinLimited(handler.TouchedEntities, MaxListItems, e => $"`{e}`")}");
            if (handler.DispatchedRequests.Count > 0)
                sb.AppendLine($"  - Dispatches: {JoinLimited(handler.DispatchedRequests, MaxListItems, d => $"`{CSharpScanner.SimpleName(d)}`")}");
        }
        sb.AppendLine();
    }

    private static void AppendValidators(
        StringBuilder sb,
        CqrsRequest request,
        IReadOnlyDictionary<string, ValidatorInfo> validatorsByFull,
        IReadOnlyDictionary<string, ValidatorInfo> validatorsBySimple)
    {
        sb.AppendLine($"### Validators ({request.Validators.Count})");
        if (request.Validators.Count == 0)
        {
            sb.AppendLine("The scan model links no validator to this request.");
            sb.AppendLine();
            return;
        }

        foreach (var name in request.Validators)
        {
            var validator = Resolve(name, validatorsByFull, validatorsBySimple);
            sb.AppendLine(validator is null
                ? $"- `{CSharpScanner.SimpleName(name)}` (unresolved — no source declaration in the scanned model)"
                : $"- `{validator.Name}`{FormatLoc(validator.Location)}{ProjectSuffix(validator.ProjectName)}");
        }
        sb.AppendLine();
    }

    private static void AppendBehaviors(
        StringBuilder sb,
        IReadOnlyList<BehaviorOrdering.SiteGroup> registered,
        IReadOnlyList<BehaviorInfo> unregistered)
    {
        var total = registered.Sum(g => g.Behaviors.Count) + unregistered.Count;
        sb.AppendLine($"### Pipeline behaviors ({total})");
        if (total == 0)
        {
            sb.AppendLine("The scan model registers no pipeline behaviors.");
            sb.AppendLine();
            return;
        }

        sb.AppendLine($"_{BehaviorPresentationText.RegisteredOrderNote}_");
        foreach (var group in registered)
        {
            foreach (var behavior in group.Behaviors)
            {
                var order = behavior.RegistrationOrder is { } value ? $"order {value}, " : "";
                sb.AppendLine($"- `{behavior.Name}` ({order}{group.Site}){FormatLoc(behavior.Location)}");
            }
        }

        if (unregistered.Count > 0)
        {
            sb.AppendLine($"_{BehaviorPresentationText.UnregisteredHeading}_");
            foreach (var behavior in unregistered)
            {
                sb.AppendLine($"- `{behavior.Name}`{FormatLoc(behavior.Location)}{ProjectSuffix(behavior.ProjectName)}");
            }
        }
        sb.AppendLine();
    }

    private static void AppendEndpoints(
        StringBuilder sb,
        CqrsRequest request,
        IReadOnlyList<EndpointInfo> endpoints,
        AnalysisResult result)
    {
        sb.AppendLine($"### Endpoints ({endpoints.Count})");
        if (endpoints.Count == 0)
        {
            sb.AppendLine("No endpoint dispatches this request in the scanned model.");
            sb.AppendLine();
            return;
        }

        foreach (var endpoint in endpoints)
        {
            var inferred = endpoint.IsInferred ? " _(inferred)_" : "";
            var edge = result.FlowEdges.FirstOrDefault(e =>
                e.Kind == FlowEdgeKind.EndpointToRequest &&
                string.Equals(e.To, request.FullName, StringComparison.Ordinal) &&
                string.Equals(e.From, endpoint.Name, StringComparison.Ordinal));
            var edgeLoc = edge is not null && edge.Location is not null
                ? $" (`{edge.Location.File}:{edge.Location.Line}`)"
                : FormatLoc(endpoint.Location);
            sb.AppendLine($"- `{endpoint.DisplayLabel}`{inferred} (`{endpoint.Name}`{edgeLoc})");
        }
        sb.AppendLine();
    }

    private sealed record PublishedNotification(string Name, CqrsNotification? Resolved);

    private static List<PublishedNotification> PublishedNotifications(
        CqrsRequest request,
        IReadOnlyList<HandlerInfo> handlers,
        IReadOnlyList<string> requestPublished,
        IReadOnlyDictionary<string, CqrsNotification> notificationsByFull,
        IReadOnlyDictionary<string, CqrsNotification> notificationsBySimple)
    {
        var names = new List<string>();
        void Add(string name)
        {
            if (!names.Contains(name, StringComparer.Ordinal))
                names.Add(name);
        }

        foreach (var name in requestPublished)
            Add(name);
        foreach (var handler in handlers)
            foreach (var name in handler.PublishedNotifications)
                Add(name);

        return names
            .Select(name => new PublishedNotification(name, Resolve(name, notificationsByFull, notificationsBySimple)))
            .ToList();
    }

    private static void AppendNotifications(
        StringBuilder sb,
        IReadOnlyList<PublishedNotification> notifications,
        IReadOnlyDictionary<string, NotificationHandlerInfo> notifHandlersByFull,
        IReadOnlyDictionary<string, NotificationHandlerInfo> notifHandlersBySimple)
    {
        sb.AppendLine($"### Notifications published ({notifications.Count})");
        if (notifications.Count == 0)
        {
            sb.AppendLine("The scan model records no notification published by this request or its handler(s).");
            sb.AppendLine();
            return;
        }

        foreach (var published in notifications)
        {
            if (published.Resolved is null)
            {
                sb.AppendLine($"- `{CSharpScanner.SimpleName(published.Name)}` " +
                              "(unresolved — no source declaration in the scanned model)");
                continue;
            }

            var notification = published.Resolved;
            sb.AppendLine($"- `{notification.Name}`{FormatLoc(notification.Location)}" +
                          ProjectSuffix(notification.ProjectName) +
                          (string.IsNullOrEmpty(notification.ViaMarker) ? "" : $" via `{notification.ViaMarker}`"));
            if (notification.Handlers.Count == 0)
            {
                sb.AppendLine("  - No notification handler is linked to it in the scanned model.");
                continue;
            }

            sb.AppendLine($"  - Handlers ({notification.Handlers.Count}):");
            foreach (var handlerName in notification.Handlers)
            {
                var handler = Resolve(handlerName, notifHandlersByFull, notifHandlersBySimple);
                sb.AppendLine(handler is null
                    ? $"    - `{CSharpScanner.SimpleName(handlerName)}` (unresolved — no source declaration in the scanned model)"
                    : $"    - `{handler.Name}`{FormatLoc(handler.Location)}");
            }
        }
        sb.AppendLine();
    }

    private sealed record DbContextEdgeRow(
        string Kind,
        string From,
        string To,
        string? Via,
        bool Resolved,
        SourceLocation? Location,
        string? Project);

    private static List<DbContextEdgeRow> DbContextEdges(AnalysisResult result, IReadOnlyList<HandlerInfo> handlers)
    {
        var handlerNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var handler in handlers)
        {
            handlerNames.Add(handler.FullName);
            handlerNames.Add(handler.Name);
        }

        var rows = new List<DbContextEdgeRow>();
        foreach (var edge in result.FlowEdges)
        {
            if (edge.Kind == FlowEdgeKind.HandlerToDbContext && handlerNames.Contains(edge.From))
                rows.Add(new DbContextEdgeRow(
                    "handlerToDbContext", edge.From, edge.To, Via: null, Resolved: false, edge.Location, edge.ProjectName));
        }

        // Resolved through the service-resolution index: the interface is named so the
        // hop is never mistaken for a direct reference.
        foreach (var edge in result.ResolvedEdges)
        {
            if (edge.Kind == FlowEdgeKind.HandlerToDbContextResolved && handlerNames.Contains(edge.From))
                rows.Add(new DbContextEdgeRow(
                    "handlerToDbContextResolved", edge.From, edge.To, edge.Via, Resolved: true, edge.Location, edge.ProjectName));
        }

        return rows
            .OrderBy(e => e.From, StringComparer.Ordinal)
            .ThenBy(e => e.To, StringComparer.Ordinal)
            .ThenBy(e => e.Via ?? "", StringComparer.Ordinal)
            .ToList();
    }

    private static void AppendDbContextEdges(StringBuilder sb, IReadOnlyList<DbContextEdgeRow> edges)
    {
        sb.AppendLine($"### DbContext edges ({edges.Count})");
        if (edges.Count == 0)
        {
            sb.AppendLine("No `handlerToDbContext` flow edge is recorded for this request's handler(s) in the scanned model.");
            sb.AppendLine();
            return;
        }

        foreach (var edge in edges)
        {
            var resolved = edge.Resolved
                ? $" (resolved through DI via `{edge.Via}`)"
                : "";
            sb.AppendLine($"- `{edge.Kind}` `{edge.From}` → `{edge.To}`{resolved}{FormatLoc(edge.Location)}" +
                          ProjectSuffix(edge.Project));
        }
        sb.AppendLine();
    }

    private sealed record Reference(string Kind, string Name, string How, SourceLocation? Location);

    private static List<Reference> ReverseReach(
        AnalysisResult result,
        CqrsRequest request,
        bool allowSimpleNameEndpointFallback)
    {
        var references = new List<Reference>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(string kind, string name, string how, SourceLocation? location)
        {
            var key = $"{kind}\u0000{name}";
            if (!seen.Add(key))
                return;
            references.Add(new Reference(kind, name, how, location));
        }

        // The endpoint catalog's own record of the dispatch (with the edge's provenance when the
        // model holds an endpointToRequest edge for it).
        foreach (var endpoint in result.Endpoints)
        {
            if (!EndpointTargetsRequest(endpoint, request, allowSimpleNameEndpointFallback))
                continue;
            var edge = result.FlowEdges.FirstOrDefault(e =>
                e.Kind == FlowEdgeKind.EndpointToRequest &&
                string.Equals(e.To, request.FullName, StringComparison.Ordinal) &&
                string.Equals(e.From, endpoint.Name, StringComparison.Ordinal));
            if (edge is not null)
            {
                Add("endpoint", endpoint.Name, "dispatches this request (`endpointToRequest` edge)", edge.Location ?? endpoint.Location);
            }
            else
            {
                Add("endpoint", endpoint.Name, "dispatches this request (endpoint target)", endpoint.Location);
            }
        }

        // An endpoint→request edge whose endpoint is not in the catalog is still a model fact.
        foreach (var edge in result.FlowEdges)
        {
            if (edge.Kind == FlowEdgeKind.EndpointToRequest &&
                string.Equals(edge.To, request.FullName, StringComparison.Ordinal))
            {
                Add("endpoint", edge.From, "dispatches this request (`endpointToRequest` edge)", edge.Location);
            }
        }

        // Handler→request flow edges (the model holds none in the frozen corpora, but the
        // relation is modelled and must be surfaced when present).
        foreach (var edge in result.FlowEdges)
        {
            if (edge.Kind == FlowEdgeKind.HandlerToRequest &&
                string.Equals(edge.To, request.FullName, StringComparison.Ordinal))
            {
                Add("handler", edge.From, "sends this request (`handlerToRequest` edge)", edge.Location);
            }
        }

        // Resolved dispatch call sites whose target is this request.
        foreach (var site in result.DispatchCallSites)
        {
            if (site.TargetKind == DispatchTargetKind.Request &&
                string.Equals(site.TargetFullName, request.FullName, StringComparison.Ordinal))
            {
                var sender = site.SenderFullName ?? "(sender unresolved)";
                Add("dispatch site", sender, "resolved dispatch call site", site.Location);
            }
        }

        // DispatchedRequests lists: handlers, notification handlers and other requests that
        // name this request as one they send.
        foreach (var handler in result.Handlers)
        {
            if (NamesRequest(handler.DispatchedRequests, request))
                Add("handler", handler.FullName, "records dispatching this request", handler.Location);
        }
        foreach (var notifHandler in result.NotificationHandlers)
        {
            if (NamesRequest(notifHandler.DispatchedRequests, request))
                Add("notification handler", notifHandler.FullName, "records dispatching this request", notifHandler.Location);
        }
        foreach (var sender in result.Requests)
        {
            if (!ReferenceEquals(sender, request) && NamesRequest(sender.DispatchedRequests, request))
                Add("request", sender.FullName, "records dispatching this request", sender.Location);
        }

        return references
            .OrderBy(r => r.Kind, StringComparer.Ordinal)
            .ThenBy(r => r.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static bool NamesRequest(IEnumerable<string> dispatched, CqrsRequest request)
    {
        foreach (var name in dispatched)
        {
            // A dispatched name is stored as a full type name when it resolved; match it exactly
            // so a duplicated simple name (e.g. two modules' CreateMemberCommand) never cross-links.
            if (string.Equals(name, request.FullName, StringComparison.Ordinal))
                return true;

            // A bare simple name carries no namespace, so it can only name this request when this
            // request's simple name is itself unambiguous in the model.
            if (!name.Contains('.') &&
                string.Equals(CSharpScanner.SimpleName(name), request.Name, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private static void AppendReverseReach(StringBuilder sb, IReadOnlyList<Reference> references, AnalysisResult result)
    {
        sb.AppendLine($"### Reverse reach ({references.Count}) — shapes that reference this request");
        if (references.Count == 0)
        {
            sb.Append("No shape in the scanned model references this request.");
            if (result.UnresolvedDispatches.Count > 0)
            {
                sb.Append($" The scan holds {result.UnresolvedDispatches.Count} unresolved dispatch call site(s) " +
                          "whose targets could not be resolved (runtime-typed send); none is attributed to this request.");
            }
            sb.AppendLine();
            sb.AppendLine();
            return;
        }

        foreach (var reference in references)
        {
            sb.AppendLine($"- {reference.Kind} `{reference.Name}` — {reference.How}{FormatLoc(reference.Location)}");
        }
        sb.AppendLine();
    }

    private static void AppendNotFound(StringBuilder sb, AnalysisResult result, string filter, bool contains)
    {
        var subject = contains ? $"matching `{filter}`" : $"named `{filter}`";
        sb.AppendLine($"No request {subject} is present in the scanned model " +
                      $"({result.Requests.Count} request(s) inventoried). No impact can be traced; " +
                      "the name is not completed or guessed.");

        var known = result.Requests
            .Select(r => r.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (known.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Known requests: {JoinLimited(known, 20, n => $"`{n}`")}." +
                          (contains ? "" : " Use `match: \"contains\"` for a substring search."));
        }
        sb.AppendLine();
    }

    private static string ProjectSuffix(string? project)
        => string.IsNullOrEmpty(project) ? "" : $" · project `{project}`";
}
