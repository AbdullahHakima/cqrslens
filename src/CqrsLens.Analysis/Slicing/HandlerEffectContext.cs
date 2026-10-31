namespace CqrsLens;

/// <summary>
/// Shared state for enriching one handler: its source position, collected effects and the
/// lookups used to resolve notifications and receivers.
/// </summary>
internal sealed class HandlerEffectContext
{
    internal HandlerEffectContext(
        CSharpScanner.ClassDecl decl,
        SourceFile file,
        CSharpScanner.FileScan scan,
        SourceMask mask,
        SliceIndex index,
        List<string> dependencies,
        List<string> touchedEntities,
        List<string> publishedNotifications,
        List<string> dispatchedRequests,
        string? selfRequestName)
    {
        Decl = decl;
        File = file;
        Scan = scan;
        Mask = mask;
        Index = index;
        Dependencies = dependencies;
        TouchedEntities = touchedEntities;
        PublishedNotifications = publishedNotifications;
        DispatchedRequests = dispatchedRequests;
        SelfRequestName = selfRequestName;
        BodyStart = decl.BodySpan.Start;
        BodyEnd = Math.Min(file.Code.Length, decl.BodySpan.End);
    }

    internal CSharpScanner.ClassDecl Decl { get; }
    internal SourceFile File { get; }
    internal CSharpScanner.FileScan Scan { get; }
    internal SourceMask Mask { get; }
    internal SliceIndex Index { get; }
    internal List<string> Dependencies { get; }
    internal List<string> TouchedEntities { get; }
    internal List<string> PublishedNotifications { get; }
    internal List<string> DispatchedRequests { get; }
    internal string? SelfRequestName { get; }

    internal int BodyStart { get; }
    internal int BodyEnd { get; }
    internal bool HasBody => BodyEnd > BodyStart;

    internal void Touch(string label)
    {
        if (!TouchedEntities.Contains(label, StringComparer.Ordinal))
            TouchedEntities.Add(label);
    }

    internal void Publish(string eventSimpleName)
    {
        if (!Index.NotificationsBySimple.TryGetValue(eventSimpleName, out var hits))
            return;
        var notif = DeepSliceCandidateResolver.PickNotification(hits, Decl.Namespace, File.ProjectName);
        if (notif is not null)
            AddNotificationAndWrappers(notif);
    }

    private void AddNotificationAndWrappers(CqrsNotification notif)
    {
        AddUnique(PublishedNotifications, notif.FullName);

        if (Index.WrapperNotificationsByInnerSimple.TryGetValue(notif.Name, out var wrappers))
        {
            var wrapper = DeepSliceCandidateResolver.PickNotification(
                wrappers, Decl.Namespace, File.ProjectName);
            if (wrapper is not null)
                AddUnique(PublishedNotifications, wrapper.FullName);
        }
    }

    private static void AddUnique(List<string> target, string value)
    {
        if (!target.Contains(value, StringComparer.Ordinal))
            target.Add(value);
    }
}
