using System.Text.RegularExpressions;
using static CqrsLens.DeepSlicePatterns;

namespace CqrsLens;

/// <summary>
/// Enriches handler and notification-handler shapes with constructor dependencies, touched
/// DbSets/entities, published notifications and dispatched requests. All receiver-based lookups
/// fail closed: a member is attributed only to the type the source actually declares or infers.
/// </summary>
internal static class HandlerSliceEnricher
{
    internal static void EnrichAll(
        List<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles,
        AnalysisResult result,
        SliceIndex index,
        Dictionary<string, List<HandlerInfo>> handlersByFull,
        Dictionary<string, List<NotificationHandlerInfo>> notifHandlersByFull)
    {
        foreach (var (file, scan, mask) in parsedFiles)
        {
            foreach (var decl in scan.Classes)
            {
                if (handlersByFull.TryGetValue(decl.FullName, out var reqHandlerCandidates))
                {
                    var handler = DeepSliceCandidateResolver.PickHandler(reqHandlerCandidates, file.ProjectName);
                    if (handler is not null)
                    {
                        EnrichTarget(
                            decl,
                            file,
                            scan,
                            mask,
                            handler.Dependencies,
                            handler.TouchedEntities,
                            handler.PublishedNotifications,
                            handler.DispatchedRequests,
                            selfRequestName: handler.RequestName,
                            index);
                    }
                }

                if (notifHandlersByFull.TryGetValue(decl.FullName, out var notifHandlerCandidates))
                {
                    var notifHandler = DeepSliceCandidateResolver.PickNotificationHandler(notifHandlerCandidates, file.ProjectName);
                    if (notifHandler is not null)
                    {
                        EnrichTarget(
                            decl,
                            file,
                            scan,
                            mask,
                            notifHandler.Dependencies,
                            notifHandler.TouchedEntities,
                            notifHandler.PublishedNotifications,
                            notifHandler.DispatchedRequests,
                            selfRequestName: null,
                            index);
                    }
                }
            }
        }

        RollUpHandlerEffects(result);
    }

    /// <summary>
    /// Rolls handler side-effects onto the requests linked to those handlers.
    /// </summary>
    private static void RollUpHandlerEffects(AnalysisResult result)
    {
        var handlerLookup = result.Handlers
            .GroupBy(h => h.FullName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        foreach (var request in result.Requests)
        {
            foreach (var handlerFullName in request.Handlers)
            {
                if (!handlerLookup.TryGetValue(handlerFullName, out var handlers))
                    continue;

                foreach (var h in handlers)
                {
                    foreach (var entity in h.TouchedEntities)
                    {
                        if (!request.TouchedEntities.Contains(entity, StringComparer.Ordinal))
                            request.TouchedEntities.Add(entity);
                    }
                    foreach (var notif in h.PublishedNotifications)
                    {
                        if (!request.PublishedNotifications.Contains(notif, StringComparer.Ordinal))
                            request.PublishedNotifications.Add(notif);
                    }
                    foreach (var dispatched in h.DispatchedRequests)
                    {
                        if (!request.DispatchedRequests.Contains(dispatched, StringComparer.Ordinal))
                            request.DispatchedRequests.Add(dispatched);
                    }
                }
            }
        }
    }

    private static void EnrichTarget(
        CSharpScanner.ClassDecl decl,
        SourceFile file,
        CSharpScanner.FileScan scan,
        SourceMask mask,
        List<string> dependencies,
        List<string> touchedEntities,
        List<string> publishedNotifications,
        List<string> dispatchedRequests,
        string? selfRequestName,
        SliceIndex index)
    {
        void Touch(string label)
        {
            if (!touchedEntities.Contains(label, StringComparer.Ordinal))
                touchedEntities.Add(label);
        }

        void Publish(string eventSimpleName)
        {
            if (!index.NotificationsBySimple.TryGetValue(eventSimpleName, out var hits))
                return;
            var notif = DeepSliceCandidateResolver.PickNotification(hits, decl.Namespace, file.ProjectName);
            if (notif is not null)
                AddNotificationAndWrappers(notif, decl.Namespace, file.ProjectName, publishedNotifications, index.WrapperNotificationsByInnerSimple);
        }

        // 1. Extract constructor / primary-constructor dependencies
        DependencyExtractor.Extract(decl, file.Code, mask, scan.Classes, dependencies);

        // 2. Resolve entities from injected repositories. Generic repositories (IRepository<T>,
        // IReadRepository<T>) resolve from their type argument. Only non-generic repositories and
        // services (IMeetingGroupProposalRepository, IDeleteContributorService) use the
        // repository -> entity map, because that map is keyed by arity-less simple type names and
        // would otherwise leak one IRepository<X> implementation's entity into every IRepository<Y>.
        foreach (var dep in dependencies)
        {
            foreach (Match gm in GenericRepositoryRegex().Matches(dep))
            {
                var entitySimple = CSharpScanner.SimpleName(gm.Groups["entity"].Value.Trim());
                if (!ReceiverTypeMapBuilder.IsEntityCandidate(entitySimple, decl, index.DbSetsByEntity)) continue;
                Touch(index.DbSetsByEntity.TryGetValue(entitySimple, out var dbLabel) ? dbLabel : entitySimple);
            }

            var depSimple = CSharpScanner.UnqualifiedTypeName(dep);
            if (string.IsNullOrEmpty(depSimple))
                continue;
            if (dep.Contains('<'))
                continue;

            if (index.RepoEntitiesByType.TryGetValue(depSimple, out var repoLabels))
            {
                foreach (var label in repoLabels)
                    Touch(label);
            }
            else
            {
                var namedMatch = NamedRepositoryRegex().Match(depSimple);
                if (namedMatch.Success)
                {
                    var stem = namedMatch.Groups["entity"].Value;
                    if (index.DbSetsByEntity.TryGetValue(stem, out var stemLabel))
                        Touch(stemLabel);
                }
            }
        }

        if (decl.BodySpan.End <= decl.BodySpan.Start)
            return;

        var bodyStart = decl.BodySpan.Start;
        var bodyEnd = Math.Min(file.Code.Length, decl.BodySpan.End);
        var receiverTypes = ReceiverTypeMapBuilder.Build(
            decl, file.Code, mask, scan.Classes, index.DbSetsByProperty, index.DbContextTypeNames, index.RepoEntitiesByType);

        // 3. Direct DbSet access. Only `<dbContext>.<DbSetProperty>` counts: a payload or DTO member
        // that merely shares a DbSet's name (`request.Orders`) is not a database touch.
        if (index.DbSetsByProperty.Count > 0 || index.DbSetsByEntity.Count > 0)
        {
            foreach (Match m in MemberAccessRegex().Matches(file.Code, bodyStart))
            {
                if (m.Index >= bodyEnd) break;
                if (!index.DbSetsByProperty.TryGetValue(m.Groups["prop"].Value, out var label)) continue;
                if (!mask.IsCode(m.Index)) continue;
                if (!ReferenceEquals(DeepSliceSyntax.InnermostClass(scan.Classes, m.Index), decl)) continue;
                if (!ReceiverTypeMapBuilder.IsDbContextReceiver(ReceiverTypeMapBuilder.GetReceiverIdentifier(file.Code, m.Index), receiverTypes, index.DbContextTypeNames)) continue;

                Touch(label);
            }

            foreach (Match m in DbContextSetMethodRegex().Matches(file.Code, bodyStart))
            {
                if (m.Index >= bodyEnd) break;
                if (!mask.IsCode(m.Index)) continue;
                if (!ReferenceEquals(DeepSliceSyntax.InnermostClass(scan.Classes, m.Index), decl)) continue;
                if (!ReceiverTypeMapBuilder.IsDbContextReceiver(ReceiverTypeMapBuilder.GetReceiverIdentifier(file.Code, m.Index), receiverTypes, index.DbContextTypeNames))
                    continue;

                var entitySimple = CSharpScanner.SimpleName(m.Groups["entity"].Value.Trim());
                if (!ReceiverTypeMapBuilder.IsEntityCandidate(entitySimple, decl, index.DbSetsByEntity))
                    continue;
                if (index.DbSetsByEntity.TryGetValue(entitySimple, out var label))
                    Touch(label);
            }
        }

        // 4. Check object creations and generic Publish/Send calls inside the handler
        foreach (var typeRef in EnumerateDispatchedTypeRefs(file.Code, bodyStart, bodyEnd, mask, scan.Classes, decl))
        {
            var simple = CSharpScanner.UnqualifiedTypeName(typeRef);

            Publish(simple);

            if (index.RequestsBySimple.TryGetValue(simple, out var reqHits))
            {
                var req = DeepSliceCandidateResolver.PickRequest(reqHits, file.ProjectName, scan.Usings);
                if (req is not null &&
                    !string.Equals(req.Name, selfRequestName, StringComparison.Ordinal) &&
                    !dispatchedRequests.Contains(req.FullName, StringComparer.Ordinal))
                {
                    dispatchedRequests.Add(req.FullName);
                }
            }

            // `new DomainEntity(...)` only publishes what that entity's *constructor* raises.
            if (index.MemberEvents.TryGetValue((simple, simple), out var ctorEventNames))
            {
                foreach (var evSimple in ctorEventNames)
                    Publish(evSimple);
            }
        }

        if (index.MemberEvents.Count == 0)
            return;

        // 5. Check static factory calls (e.g., MeetingGroupProposal.ProposeNew(...))
        foreach (Match sm in StaticMethodCallRegex().Matches(file.Code, bodyStart))
        {
            if (sm.Index >= bodyEnd) break;
            if (!mask.IsCode(sm.Index)) continue;
            if (!ReferenceEquals(DeepSliceSyntax.InnermostClass(scan.Classes, sm.Index), decl)) continue;

            var targetType = sm.Groups["type"].Value;
            if (!index.MemberEvents.TryGetValue((targetType, sm.Groups["method"].Value), out var staticEvents))
                continue;
            foreach (var evSimple in staticEvents)
                Publish(evSimple);
        }

        // 6. Instance member calls and property sets (`order.Update()`, `entity.Done = true`). The
        // member is matched only against the receiver's declared or inferred type, never against every
        // active type, so `order.Update()` cannot report `Customer.Update()`'s event.
        foreach (Match mm in MemberAccessRegex().Matches(file.Code, bodyStart))
        {
            if (mm.Index >= bodyEnd) break;
            if (!mask.IsCode(mm.Index)) continue;
            if (!ReferenceEquals(DeepSliceSyntax.InnermostClass(scan.Classes, mm.Index), decl)) continue;

            var member = mm.Groups["prop"].Value;
            var receiver = ReceiverTypeMapBuilder.GetReceiverIdentifier(file.Code, mm.Index);
            foreach (var receiverType in ReceiverTypeMapBuilder.ResolveReceiverTypes(receiver, receiverTypes))
            {
                if (!index.MemberEvents.TryGetValue((receiverType, member), out var raisedEvents))
                    continue;
                foreach (var evSimple in raisedEvents)
                    Publish(evSimple);
            }
        }
    }

    private static void AddNotificationAndWrappers(
        CqrsNotification notif,
        string? callerNamespace,
        string? projectName,
        List<string> publishedNotifications,
        Dictionary<string, List<CqrsNotification>> wrapperNotificationsByInnerSimple)
    {
        if (!publishedNotifications.Contains(notif.FullName, StringComparer.Ordinal))
            publishedNotifications.Add(notif.FullName);

        if (wrapperNotificationsByInnerSimple.TryGetValue(notif.Name, out var wrappers))
        {
            var wrapper = DeepSliceCandidateResolver.PickNotification(wrappers, callerNamespace, projectName);
            if (wrapper is not null && !publishedNotifications.Contains(wrapper.FullName, StringComparer.Ordinal))
                publishedNotifications.Add(wrapper.FullName);
        }
    }

    private static IEnumerable<string> EnumerateDispatchedTypeRefs(
        string code,
        int bodyStart,
        int bodyEnd,
        SourceMask mask,
        List<CSharpScanner.ClassDecl> classes,
        CSharpScanner.ClassDecl owner)
    {
        foreach (Match m in ObjectCreationRegex().Matches(code, bodyStart))
        {
            if (m.Index >= bodyEnd) break;
            if (!mask.IsCode(m.Index)) continue;
            if (!ReferenceEquals(DeepSliceSyntax.InnermostClass(classes, m.Index), owner)) continue;
            yield return m.Groups["type"].Value;
        }

        foreach (Match m in GenericDispatchRegex().Matches(code, bodyStart))
        {
            if (m.Index >= bodyEnd) break;
            if (!mask.IsCode(m.Index)) continue;
            if (!ReferenceEquals(DeepSliceSyntax.InnermostClass(classes, m.Index), owner)) continue;
            yield return m.Groups["type"].Value;
        }
    }
}
