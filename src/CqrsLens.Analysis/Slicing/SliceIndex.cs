namespace CqrsLens;

/// <summary>
/// Shared lookups for one deep-slice run, so slice enrichment does not have to thread a dozen
/// dictionaries through every call.
/// </summary>
internal sealed record SliceIndex(
    Dictionary<string, string> DbSetsByProperty,
    Dictionary<string, string> DbSetsByEntity,
    HashSet<string> DbContextTypeNames,
    Dictionary<string, List<string>> RepoEntitiesByType,
    Dictionary<string, List<CqrsNotification>> NotificationsBySimple,
    Dictionary<string, List<CqrsNotification>> WrapperNotificationsByInnerSimple,
    Dictionary<(string TypeName, string MemberName), List<string>> MemberEvents,
    Dictionary<string, List<CqrsRequest>> RequestsBySimple);
