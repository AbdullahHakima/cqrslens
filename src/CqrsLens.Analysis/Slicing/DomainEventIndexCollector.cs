namespace CqrsLens;

/// <summary>
/// Accumulates the second-level domain index while excluding simple type names that are ambiguous
/// across the solution.
/// </summary>
internal sealed class DomainEventIndexCollector
{
    private readonly HashSet<string> _ambiguousTypeNames;

    internal DomainEventIndexCollector(HashSet<string> ambiguousTypeNames)
    {
        _ambiguousTypeNames = ambiguousTypeNames;
    }

    internal Dictionary<string, List<string>> RepoEntitiesByType { get; } =
        new(StringComparer.Ordinal);

    internal Dictionary<string, List<CqrsNotification>> WrapperNotificationsByInnerSimple { get; } =
        new(StringComparer.Ordinal);

    internal Dictionary<(string TypeName, string MemberName), List<string>> MemberEvents { get; } = new();

    internal void AddRepoEntity(string typeName, string label)
    {
        if (string.IsNullOrEmpty(typeName) || string.IsNullOrEmpty(label))
            return;
        if (_ambiguousTypeNames.Contains(typeName))
            return;

        var list = GetOrAdd(RepoEntitiesByType, typeName);
        if (!list.Contains(label, StringComparer.Ordinal))
            list.Add(label);
    }

    internal void AddMemberEvent(string typeName, string memberName, string eventSimpleName)
    {
        if (string.IsNullOrEmpty(typeName) || string.IsNullOrEmpty(memberName) || string.IsNullOrEmpty(eventSimpleName))
            return;
        if (_ambiguousTypeNames.Contains(typeName))
            return;

        var list = GetOrAdd(MemberEvents, (typeName, memberName));
        if (!list.Contains(eventSimpleName, StringComparer.Ordinal))
            list.Add(eventSimpleName);
    }

    internal void AddWrapper(string innerSimple, CqrsNotification notification)
    {
        var list = GetOrAdd(WrapperNotificationsByInnerSimple, innerSimple);
        if (!list.Contains(notification))
            list.Add(notification);
    }

    internal DomainEventIndex ToIndex() =>
        new(RepoEntitiesByType, WrapperNotificationsByInnerSimple, MemberEvents);

    private static List<string> GetOrAdd(
        Dictionary<string, List<string>> map,
        string key)
    {
        if (!map.TryGetValue(key, out var list))
        {
            list = new List<string>();
            map[key] = list;
        }
        return list;
    }

    private static List<string> GetOrAdd(
        Dictionary<(string TypeName, string MemberName), List<string>> map,
        (string TypeName, string MemberName) key)
    {
        if (!map.TryGetValue(key, out var list))
        {
            list = new List<string>();
            map[key] = list;
        }
        return list;
    }

    private static List<CqrsNotification> GetOrAdd(
        Dictionary<string, List<CqrsNotification>> map,
        string key)
    {
        if (!map.TryGetValue(key, out var list))
        {
            list = new List<CqrsNotification>();
            map[key] = list;
        }
        return list;
    }
}
