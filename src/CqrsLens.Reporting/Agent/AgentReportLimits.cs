namespace CqrsLens;

/// <summary>Shared output budgets and filter vocabulary for agent reports.</summary>
internal static class AgentReportLimits
{
    internal const int DefaultSliceLimit = 5;
    internal const int MaxSliceLimit = 25;
    internal const int MaxTraceCharacters = 14_000;
    internal const int MaxListItems = 12;

    /// <summary>Maximum page size for a filtered <c>check_architecture</c> query.</summary>
    internal const int MaxCheckLimit = 200;

    /// <summary>Maximum page size for each item list of a <c>diff_architecture</c> answer.</summary>
    internal const int MaxDiffLimit = 200;

    internal static readonly HashSet<string> GenericKindFilters = new(StringComparer.OrdinalIgnoreCase)
    {
        "Command", "Query", "Request", "Handler", "Notification", "Event", "CommandHandler", "QueryHandler",
    };
}