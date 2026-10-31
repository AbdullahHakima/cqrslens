using static CqrsLens.AgentReportLimits;
using static CqrsLens.AgentReportLookups;

namespace CqrsLens;

/// <summary>Shared formatting and matching primitives for agent reports.</summary>
internal static class AgentReportFormatter
{
    internal static string JoinLimited<T>(IEnumerable<T> items, int limit, Func<T, string> format)
    {
        var list = items as IList<T> ?? items.ToList();
        var text = string.Join(", ", list.Take(limit).Select(format));
        return list.Count > limit ? $"{text} … +{list.Count - limit} more" : text;
    }

    internal static string EntityNameFromLabel(string label)
    {
        var open = label.IndexOf('(');
        var close = label.LastIndexOf(')');
        return open >= 0 && close > open + 1
            ? label[(open + 1)..close].Trim()
            : label.Trim();
    }

    internal static string EntityPropertyFromLabel(string label)
    {
        var open = label.IndexOf('(');
        return open > 0 ? label[..open].Trim() : "";
    }

    internal static bool TypeContainsName(string type, string name)
    {
        var tokenStart = -1;
        for (var i = 0; i <= type.Length; i++)
        {
            var isTokenChar = i < type.Length &&
                              (char.IsLetterOrDigit(type[i]) || type[i] == '_');
            if (isTokenChar)
            {
                if (tokenStart < 0) tokenStart = i;
                continue;
            }

            if (tokenStart >= 0)
            {
                if (type.AsSpan(tokenStart, i - tokenStart).Equals(name, StringComparison.OrdinalIgnoreCase))
                    return true;
                tokenStart = -1;
            }
        }
        return false;
    }

    internal static string FormatNotificationHandlerSummary(
        string nhName,
        IReadOnlyDictionary<string, NotificationHandlerInfo> notifHandlersByFull,
        IReadOnlyDictionary<string, NotificationHandlerInfo> notifHandlersBySimple)
    {
        var nh = Resolve(nhName, notifHandlersByFull, notifHandlersBySimple);
        var baseText = $"`{CSharpScanner.SimpleName(nhName)}`{FormatLoc(nh?.Location)}";
        if (nh is null) return baseText;

        var details = new List<string>();
        if (nh.TouchedEntities.Count > 0)
            details.Add($"Touches: {JoinLimited(nh.TouchedEntities, MaxListItems, e => $"`{e}`")}");
        if (nh.PublishedNotifications.Count > 0)
            details.Add($"Publishes: {JoinLimited(nh.PublishedNotifications, MaxListItems, n => $"`{CSharpScanner.SimpleName(n)}`")}");
        if (nh.DispatchedRequests.Count > 0)
            details.Add($"Dispatches: {JoinLimited(nh.DispatchedRequests, MaxListItems, d => $"`{CSharpScanner.SimpleName(d)}`")}");

        return details.Count == 0 ? baseText : $"{baseText} [{string.Join("; ", details)}]";
    }

    internal static bool EndpointTargetsRequest(
        EndpointInfo endpoint,
        CqrsRequest request,
        bool allowSimpleNameFallback)
    {
        if (!string.IsNullOrEmpty(endpoint.TargetRequestFullName))
            return string.Equals(endpoint.TargetRequestFullName, request.FullName, StringComparison.Ordinal);

        if (!string.IsNullOrEmpty(endpoint.TargetRequestNamespace))
            return string.Equals(endpoint.TargetRequestNamespace, request.Namespace, StringComparison.Ordinal);

        // No namespace information at all: a simple-name match is safe only when the name is unique.
        return allowSimpleNameFallback &&
               string.Equals(endpoint.TargetRequestName, request.Name, StringComparison.Ordinal);
    }

    internal static string FormatEndpoint(EndpointInfo endpoint)
    {
        var inferred = endpoint.IsInferred ? " _(inferred)_" : "";
        return $"`{endpoint.DisplayLabel}`{inferred} (`{endpoint.Name}`{FormatLoc(endpoint.Location)})";
    }

    internal static string FormatLoc(SourceLocation? loc)
        => loc is null ? "" : $" (`{loc.File}:{loc.Line}`)";
}