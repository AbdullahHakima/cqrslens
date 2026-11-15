namespace CqrsLens;

/// <summary>
/// Shared state for enriching one handler: its source position, collected effects and the
/// lookups used to resolve notifications and receivers.
/// </summary>
internal sealed class HandlerEffectContext
{
    private SourceLocation? _defaultLocation;

    internal HandlerEffectContext(
        AnalysisResult result,
        ComponentId ownerId,
        CSharpScanner.ClassDecl decl,
        SourceFile file,
        CSharpScanner.FileScan scan,
        SourceMask mask,
        SliceIndex index,
        List<string> dependencies,
        IList<string> touchedEntities,
        IList<string> publishedNotifications,
        IList<string> dispatchedRequests,
        string? selfRequestName)
    {
        Result = result;
        OwnerId = ownerId;
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

    internal AnalysisResult Result { get; }
    internal ComponentId OwnerId { get; }
    internal CSharpScanner.ClassDecl Decl { get; }
    internal SourceFile File { get; }
    internal CSharpScanner.FileScan Scan { get; }
    internal SourceMask Mask { get; }
    internal SliceIndex Index { get; }
    internal List<string> Dependencies { get; }
    internal IList<string> TouchedEntities { get; }
    internal IList<string> PublishedNotifications { get; }
    internal IList<string> DispatchedRequests { get; }
    internal string? SelfRequestName { get; }

    internal int BodyStart { get; }
    internal int BodyEnd { get; }
    internal bool HasBody => BodyEnd > BodyStart;

    internal SourceLocation? DefaultLocation =>
        _defaultLocation ??= SourceLocation.Create(File.Path, Result.RootPath, File.Code, Decl.NameIndex);

    internal SourceLocation? LocationAt(int index)
        => SourceLocation.Create(File.Path, Result.RootPath, File.Code, index) ?? DefaultLocation;

    internal void Touch(
        string label,
        SourceLocation? location = null,
        IReadOnlyList<NodeReference>? via = null,
        EvidenceStrength strength = EvidenceStrength.Inferred)
    {
        var target = Result.IdentityResolver.ResolveTouchesDataTarget(label, File.ProjectName);
        Result.Spine.AddOrMergeRelation(
            RelationKind.TouchesData,
            NodeReference.ForComponent(OwnerId),
            target,
            SpineIdentityResolver.SyntheticOccurrence(
                EvidenceRule.DeepSliceDataAccess,
                EvidenceOrigin.CallSite,
                location ?? DefaultLocation,
                strength,
                via,
                scopeProjectName: File.ProjectName));
    }

    internal void Publish(
        string eventSimpleName,
        SourceLocation? location = null,
        EvidenceStrength strength = EvidenceStrength.Inferred)
    {
        if (!Index.NotificationsBySimple.TryGetValue(eventSimpleName, out var hits))
            return;
        var notif = DeepSliceCandidateResolver.PickNotification(hits, Decl.Namespace, File.ProjectName);
        if (notif is not null)
            AddNotificationAndWrappers(notif, location, strength);
    }

    internal void Dispatch(
        CqrsRequest request,
        SourceLocation? location = null,
        EvidenceStrength strength = EvidenceStrength.Proven)
    {
        var reqRef = request.SpineId is not null
            ? NodeReference.ForComponent(request.SpineId)
            : Result.IdentityResolver.ResolveComponentOrExternalRef(request.FullName, ComponentRole.Request, request.ProjectName);

        Result.Spine.AddOrMergeRelation(
            RelationKind.Dispatches,
            NodeReference.ForComponent(OwnerId),
            reqRef,
            SpineIdentityResolver.SyntheticOccurrence(
                EvidenceRule.DeepSliceDataAccess,
                EvidenceOrigin.CallSite,
                location ?? DefaultLocation,
                strength,
                scopeProjectName: File.ProjectName));
    }

    private void AddNotificationAndWrappers(
        CqrsNotification notif,
        SourceLocation? location,
        EvidenceStrength strength)
    {
        RecordPublish(notif, location, strength);

        if (Index.WrapperNotificationsByInnerSimple.TryGetValue(notif.Name, out var wrappers))
        {
            var wrapper = DeepSliceCandidateResolver.PickNotification(
                wrappers, Decl.Namespace, File.ProjectName);
            if (wrapper is not null)
                RecordPublish(wrapper, location, strength);
        }
    }

    private void RecordPublish(CqrsNotification notif, SourceLocation? location, EvidenceStrength strength)
    {
        var notifRef = notif.SpineId is not null
            ? NodeReference.ForComponent(notif.SpineId)
            : Result.IdentityResolver.ResolveComponentOrExternalRef(notif.FullName, ComponentRole.Notification, notif.ProjectName);

        Result.Spine.AddOrMergeRelation(
            RelationKind.Publishes,
            NodeReference.ForComponent(OwnerId),
            notifRef,
            SpineIdentityResolver.SyntheticOccurrence(
                EvidenceRule.DeepSliceDataAccess,
                EvidenceOrigin.CallSite,
                location ?? DefaultLocation,
                strength,
                scopeProjectName: File.ProjectName));
    }
}
