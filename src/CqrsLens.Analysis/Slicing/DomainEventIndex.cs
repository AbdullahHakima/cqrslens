namespace CqrsLens;

/// <summary>
/// Second-level domain lookups keyed by simple type names. Ambiguous names are
/// excluded by the builder rather than attributed to an arbitrary module.
/// </summary>
internal sealed record DomainEventIndex(
    Dictionary<string, List<string>> RepoEntitiesByType,
    Dictionary<string, List<CqrsNotification>> WrapperNotificationsByInnerSimple,
    Dictionary<(string TypeName, string MemberName), List<string>> MemberEvents);
