using System.Text;
using static CqrsLens.AgentReportFormatter;
using static CqrsLens.AgentReportLimits;
using static CqrsLens.AgentReportLookups;

namespace CqrsLens;

/// <summary>Writes one end-to-end vertical slice trace.</summary>
internal static class AgentSliceWriter
{
    internal static void AppendSlice(
        StringBuilder sb,
        CqrsRequest r,
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
        var response = string.IsNullOrEmpty(r.ResponseType) ? "void" : r.ResponseType;
        sb.AppendLine($"### [{r.Kind}] `{r.Name}` → `{response}`");

        var contractLoc = FormatLoc(r.Location);
        var projLabel = string.IsNullOrEmpty(r.ProjectName) ? "" : $" [Project: `{r.ProjectName}`]";
        var markerLabel = string.IsNullOrEmpty(r.ViaMarker) ? "" : $" [Marker: `{r.ViaMarker}`]";
        sb.AppendLine($"- **Contract**: `{r.FullName}`{contractLoc}{projLabel}{markerLabel}");

        if (r.Properties.Count > 0)
        {
            var props = JoinLimited(
                r.Properties,
                20,
                p => $"`{p.Name}: {p.Type}`");
            sb.AppendLine($"- **Payload**: {props}");
        }

        if (endpointsByRequest.TryGetValue(r.Name, out var eps) && eps.Count > 0)
        {
            var epItems = eps
                .Where(e => EndpointTargetsRequest(e, r, allowSimpleNameEndpointFallback))
                .Select(FormatEndpoint)
                .ToList();
            if (epItems.Count > 0)
                sb.AppendLine($"- **Entrypoints**: {JoinLimited(epItems, 5, e => e)}");
        }
        else if (r.Entrypoints.Count > 0)
        {
            sb.AppendLine($"- **Entrypoints**: {JoinLimited(r.Entrypoints, 5, e => $"`{e}`")}");
        }

        if (r.Validators.Count > 0)
        {
            var valItems = r.Validators.Select(vName =>
            {
                var v = Resolve(vName, validatorsByFull, validatorsBySimple);
                return $"`{CSharpScanner.SimpleName(vName)}`{FormatLoc(v?.Location)}";
            });
            sb.AppendLine($"- **Validators**: {JoinLimited(valItems, 5, v => v)}");
        }

        if (r.Handlers.Count == 0)
        {
            sb.AppendLine("- **Handlers**: _None (MissingHandler)_");
        }
        else
        {
            foreach (var hName in r.Handlers.Take(5))
            {
                var h = Resolve(hName, handlersByFull, handlersBySimple);
                var hSimple = CSharpScanner.SimpleName(hName);
                var hProj = !string.IsNullOrEmpty(h?.ProjectName) && h!.ProjectName != r.ProjectName
                    ? $" [Project: `{h.ProjectName}`]"
                    : "";
                sb.AppendLine($"- **Handler**: `{hSimple}`{FormatLoc(h?.Location)}{hProj}");

                if (h is null)
                    continue;

                if (h.Dependencies.Count > 0)
                    sb.AppendLine($"  - **Dependencies**: {JoinLimited(h.Dependencies, MaxListItems, d => $"`{d}`")}");
                if (h.TouchedEntities.Count > 0)
                    sb.AppendLine($"  - **Touches DbSets**: {JoinLimited(h.TouchedEntities, MaxListItems, e => $"`{e}`")}");
                if (h.PublishedNotifications.Count > 0)
                {
                    var notifSummaries = h.PublishedNotifications.Take(10).Select(nFull =>
                    {
                        var notif = Resolve(nFull, notificationsByFull, notificationsBySimple);
                        var nSimple = CSharpScanner.SimpleName(nFull);
                        if (notif is null || notif.Handlers.Count == 0)
                            return $"`{nSimple}` (no handlers)";
                        var nhList = notif.Handlers.Take(3).Select(nhName =>
                            FormatNotificationHandlerSummary(nhName, notifHandlersByFull, notifHandlersBySimple));
                        var extra = notif.Handlers.Count > 3 ? $" … +{notif.Handlers.Count - 3} more" : "";
                        return $"`{nSimple}` → {string.Join(", ", nhList)}{extra}";
                    });
                    var eventText = string.Join("; ", notifSummaries);
                    if (h.PublishedNotifications.Count > 10)
                        eventText += $" … +{h.PublishedNotifications.Count - 10} more";
                    sb.AppendLine($"  - **Publishes Events**: {eventText}");
                }
                if (h.DispatchedRequests.Count > 0)
                {
                    var dispatched = h.DispatchedRequests.Select(d => $"`{CSharpScanner.SimpleName(d)}`");
                    sb.AppendLine($"  - **Dispatches Requests**: {JoinLimited(dispatched, MaxListItems, d => d)}");
                }
            }

            if (r.Handlers.Count > 5)
                sb.AppendLine($"  - _+{r.Handlers.Count - 5} more handler(s)._");
        }

        sb.AppendLine();
    }
}