namespace CqrsLens;

/// <summary>Shared output budgets and filter vocabulary for agent reports.</summary>
internal static class AgentReportLimits
{
    internal const int DefaultSliceLimit = 5;
    internal const int MaxSliceLimit = 25;
    internal const int MaxTraceCharacters = 14_000;
    internal const int MaxListItems = 12;

    internal static readonly HashSet<string> GenericKindFilters = new(StringComparer.OrdinalIgnoreCase)
    {
        "Command", "Query", "Request", "Handler", "Notification", "Event", "CommandHandler", "QueryHandler",
    };
}