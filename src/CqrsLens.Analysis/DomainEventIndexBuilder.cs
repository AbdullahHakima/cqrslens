using System.Text.RegularExpressions;
using static CqrsLens.DeepSlicePatterns;

namespace CqrsLens;

/// <summary>
/// Builds the second-level domain index: repository type → entity labels, notification wrapper
/// relationships, and domain member → raised-event mappings. These lookups are keyed by simple
/// type names, so ambiguous type names are excluded rather than attributed to an arbitrary module.
/// </summary>
internal static class DomainEventIndexBuilder
{
    internal static DomainEventIndex Build(
        List<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles,
        Dictionary<string, string> dbSetsByProperty,
        Dictionary<string, string> dbSetsByEntity,
        HashSet<string> dbContextTypeNames,
        Dictionary<string, List<CqrsNotification>> notificationsBySimple,
        Dictionary<string, List<HandlerInfo>> handlersByFull,
        Dictionary<string, List<NotificationHandlerInfo>> notifHandlersByFull,
        HashSet<string> ambiguousTypeNames)
    {
        var repoEntitiesByType = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var wrapperNotificationsByInnerSimple = new Dictionary<string, List<CqrsNotification>>(StringComparer.Ordinal);
        var memberEvents = new Dictionary<(string TypeName, string MemberName), List<string>>();

        void AddRepoEntity(string typeName, string label)
        {
            if (string.IsNullOrEmpty(typeName) || string.IsNullOrEmpty(label)) return;
            if (ambiguousTypeNames.Contains(typeName)) return;
            if (!repoEntitiesByType.TryGetValue(typeName, out var list))
            {
                list = new List<string>();
                repoEntitiesByType[typeName] = list;
            }
            if (!list.Contains(label, StringComparer.Ordinal))
                list.Add(label);
        }

        void AddMemberEvent(string typeName, string memberName, string eventSimpleName)
        {
            if (string.IsNullOrEmpty(typeName) || string.IsNullOrEmpty(memberName) || string.IsNullOrEmpty(eventSimpleName))
                return;
            if (ambiguousTypeNames.Contains(typeName))
                return;
            var key = (typeName, memberName);
            if (!memberEvents.TryGetValue(key, out var list))
            {
                list = new List<string>();
                memberEvents[key] = list;
            }
            if (!list.Contains(eventSimpleName, StringComparer.Ordinal))
                list.Add(eventSimpleName);
        }

        // Pass 1: Scan all classes for:
        // - Generic notification wrappers (e.g., DomainNotificationBase<TDomainEvent>)
        // - Repository implementations & services that touch DbSets/IRepository<T>
        // - Domain entity & service members that directly instantiate/publish notifications
        var domainClassesToPropagate = new List<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask, CSharpScanner.ClassDecl Decl)>();

        foreach (var (file, scan, mask) in parsedFiles)
        {
            foreach (var decl in scan.Classes)
            {
                // 1a. Check if this class is a generic notification wrapper (e.g., FooNotification : DomainNotificationBase<FooDomainEvent>)
                if (notificationsBySimple.TryGetValue(decl.Name, out var selfNotifs))
                {
                    var matchingSelf = selfNotifs.FirstOrDefault(n => string.Equals(n.FullName, decl.FullName, StringComparison.Ordinal));
                    if (matchingSelf is not null)
                    {
                        foreach (var bt in CSharpScanner.SplitTopLevel(decl.BaseList))
                        {
                            var open = bt.IndexOf('<');
                            var close = bt.LastIndexOf('>');
                            if (open > 0 && close > open + 1)
                            {
                                var innerSimple = CSharpScanner.SimpleName(bt[(open + 1)..close].Trim());
                                if (notificationsBySimple.ContainsKey(innerSimple))
                                {
                                    if (!wrapperNotificationsByInnerSimple.TryGetValue(innerSimple, out var wrapList))
                                    {
                                        wrapList = new List<CqrsNotification>();
                                        wrapperNotificationsByInnerSimple[innerSimple] = wrapList;
                                    }
                                    if (!wrapList.Contains(matchingSelf))
                                        wrapList.Add(matchingSelf);
                                }
                            }
                        }
                    }
                }

                if (handlersByFull.ContainsKey(decl.FullName) || notifHandlersByFull.ContainsKey(decl.FullName))
                    continue;

                if (decl.BodySpan.End <= decl.BodySpan.Start)
                    continue;

                var bodyStart = decl.BodySpan.Start;
                var bodyEnd = Math.Min(file.Code.Length, decl.BodySpan.End);

                // 1b. Check if this class is a Repository or Service touching entities
                var classDeps = new List<string>();
                DependencyExtractor.Extract(decl, file.Code, mask, scan.Classes, classDeps);
                var classTouchedLabels = new List<string>();

                void TouchClass(string label)
                {
                    if (!classTouchedLabels.Contains(label, StringComparer.Ordinal))
                        classTouchedLabels.Add(label);
                }

                foreach (var typeRef in classDeps.Concat(CSharpScanner.SplitTopLevel(decl.BaseList)))
                {
                    foreach (Match gm in GenericRepositoryRegex().Matches(typeRef))
                    {
                        var entitySimple = CSharpScanner.SimpleName(gm.Groups["entity"].Value.Trim());
                        if (!ReceiverTypeMapBuilder.IsEntityCandidate(entitySimple, decl, dbSetsByEntity)) continue;
                        TouchClass(dbSetsByEntity.TryGetValue(entitySimple, out var dbLabel) ? dbLabel : entitySimple);
                    }
                }

                var namedRepo = NamedRepositoryRegex().Match(decl.Name);
                if (namedRepo.Success && dbSetsByEntity.TryGetValue(namedRepo.Groups["entity"].Value, out var stemLabel))
                    TouchClass(stemLabel);

                if (dbSetsByProperty.Count > 0 || dbSetsByEntity.Count > 0)
                {
                    Dictionary<string, string>? receiverTypes = null;
                    foreach (Match m in MemberAccessRegex().Matches(file.Code, bodyStart))
                    {
                        if (m.Index >= bodyEnd) break;
                        if (!dbSetsByProperty.TryGetValue(m.Groups["prop"].Value, out var label)) continue;
                        if (!mask.IsCode(m.Index)) continue;
                        if (!ReferenceEquals(DeepSliceSyntax.InnermostClass(scan.Classes, m.Index), decl)) continue;

                        // Built lazily: most classes never reference a DbSet-named member.
                        receiverTypes ??= ReceiverTypeMapBuilder.Build(
                            decl, file.Code, mask, scan.Classes, dbSetsByProperty, dbContextTypeNames, repoEntitiesByType);
                        if (ReceiverTypeMapBuilder.IsDbContextReceiver(ReceiverTypeMapBuilder.GetReceiverIdentifier(file.Code, m.Index), receiverTypes, dbContextTypeNames))
                            TouchClass(label);
                    }

                    foreach (Match m in DbContextSetMethodRegex().Matches(file.Code, bodyStart))
                    {
                        if (m.Index >= bodyEnd) break;
                        if (!mask.IsCode(m.Index)) continue;
                        if (!ReferenceEquals(DeepSliceSyntax.InnermostClass(scan.Classes, m.Index), decl)) continue;

                        receiverTypes ??= ReceiverTypeMapBuilder.Build(
                            decl, file.Code, mask, scan.Classes, dbSetsByProperty, dbContextTypeNames, repoEntitiesByType);
                        if (!ReceiverTypeMapBuilder.IsDbContextReceiver(ReceiverTypeMapBuilder.GetReceiverIdentifier(file.Code, m.Index), receiverTypes, dbContextTypeNames))
                            continue;

                        var entitySimple = CSharpScanner.SimpleName(m.Groups["entity"].Value.Trim());
                        if (!ReceiverTypeMapBuilder.IsEntityCandidate(entitySimple, decl, dbSetsByEntity))
                            continue;
                        if (dbSetsByEntity.TryGetValue(entitySimple, out var label))
                            TouchClass(label);
                    }
                }

                // Register the class and its non-generic interfaces (IOrderRepository, IDeleteService).
                // Generic interfaces (IRepository<Order>) are skipped: the map is keyed by simple name,
                // so registering them would attach Order to every IRepository<X> consumer.
                var nonGenericInterfaces = ReceiverTypeMapBuilder.NonGenericInterfaceNames(decl);
                foreach (var label in classTouchedLabels)
                {
                    AddRepoEntity(decl.Name, label);
                    foreach (var iface in nonGenericInterfaces)
                        AddRepoEntity(iface, label);
                }

                // 1c. Check if any member inside `decl` instantiates or dispatches a CqrsNotification
                var hasDirectOrFactoryCandidate = false;
                if (ContainsObjectCreation(file.Code, bodyStart, bodyEnd - bodyStart))
                {
                    foreach (Match m in ObjectCreationRegex().Matches(file.Code, bodyStart))
                    {
                        if (m.Index >= bodyEnd) break;
                        if (!mask.IsCode(m.Index)) continue;
                        if (!ReferenceEquals(DeepSliceSyntax.InnermostClass(scan.Classes, m.Index), decl)) continue;

                        var createdSimple = CSharpScanner.UnqualifiedTypeName(m.Groups["type"].Value);
                        if (notificationsBySimple.ContainsKey(createdSimple))
                        {
                            var memberName = FindEnclosingMemberName(file.Code, mask, decl, m.Index);
                            if (!string.IsNullOrEmpty(memberName))
                            {
                                AddMemberEvent(decl.Name, memberName, createdSimple);
                                foreach (var iface in nonGenericInterfaces)
                                    AddMemberEvent(iface, memberName, createdSimple);
                            }
                        }
                        else
                        {
                            hasDirectOrFactoryCandidate = true;
                        }
                    }
                }

                if (hasDirectOrFactoryCandidate)
                    domainClassesToPropagate.Add((file, scan, mask, decl));
            }
        }

        // Pass 2: Propagate events from domain constructors and static factories (2 hops, e.g.
        // new MeetingGroupProposal -> ProposeNew, and new MeetingGroup -> CreateBasedOnProposal -> CreateMeetingGroup)
        for (var hop = 0; hop < 2 && memberEvents.Count > 0; hop++)
        {
            foreach (var (file, scan, mask, decl) in domainClassesToPropagate)
            {
                var bodyStart = decl.BodySpan.Start;
                var bodyEnd = Math.Min(file.Code.Length, decl.BodySpan.End);

                // Propagate `new TargetClass(...)` constructor events to enclosing factory methods
                foreach (Match m in ObjectCreationRegex().Matches(file.Code, bodyStart))
                {
                    if (m.Index >= bodyEnd) break;
                    if (!mask.IsCode(m.Index)) continue;
                    if (!ReferenceEquals(DeepSliceSyntax.InnermostClass(scan.Classes, m.Index), decl)) continue;

                    var targetSimple = CSharpScanner.UnqualifiedTypeName(m.Groups["type"].Value);
                    if (memberEvents.TryGetValue((targetSimple, targetSimple), out var ctorEvents))
                    {
                        var callerMember = FindEnclosingMemberName(file.Code, mask, decl, m.Index);
                        if (!string.IsNullOrEmpty(callerMember))
                        {
                            foreach (var ev in ctorEvents.ToList())
                                AddMemberEvent(decl.Name, callerMember, ev);
                        }
                    }
                }

                // Propagate `TargetClass.StaticFactory(...)` events to enclosing methods
                foreach (Match sm in StaticMethodCallRegex().Matches(file.Code, bodyStart))
                {
                    if (sm.Index >= bodyEnd) break;
                    if (!mask.IsCode(sm.Index)) continue;
                    if (!ReferenceEquals(DeepSliceSyntax.InnermostClass(scan.Classes, sm.Index), decl)) continue;

                    var targetType = sm.Groups["type"].Value;
                    var targetMethod = sm.Groups["method"].Value;
                    if (memberEvents.TryGetValue((targetType, targetMethod), out var staticEvents))
                    {
                        var callerMember = FindEnclosingMemberName(file.Code, mask, decl, sm.Index);
                        if (!string.IsNullOrEmpty(callerMember))
                        {
                            foreach (var ev in staticEvents.ToList())
                                AddMemberEvent(decl.Name, callerMember, ev);
                        }
                    }
                }
            }
        }

        return new DomainEventIndex(repoEntitiesByType, wrapperNotificationsByInnerSimple, memberEvents);
    }

    private static string? FindEnclosingMemberName(
        string code,
        SourceMask mask,
        CSharpScanner.ClassDecl decl,
        int targetIndex)
    {
        // BodySpan starts at the declaration keyword, not at the body brace. Member headers must be
        // read from inside the body; otherwise the class brace itself is taken as the first "member"
        // and every event in the class is attributed to `ClassName` (i.e. to the constructor).
        var end = Math.Min(code.Length, decl.BodySpan.End);
        var open = FindBodyOpenBrace(code, mask, decl.BodySpan.Start, end);
        if (open < 0 || targetIndex <= open)
            return null;

        var start = open + 1;
        var headerStart = start;

        for (var i = start; i < end; i++)
        {
            if (!mask.IsCode(i)) continue;
            var ch = code[i];

            if (ch == '(')
            {
                var closeParen = DeepSliceSyntax.FindMatchingParen(code, mask, i);
                if (closeParen > i)
                    i = closeParen;
                continue;
            }

            if (ch == '{')
            {
                var closeBrace = mask.FindMatchingBrace(i);
                if (closeBrace <= i) break;

                if (targetIndex >= headerStart && targetIndex <= closeBrace)
                    return ExtractMemberNameFromHeader(code[headerStart..i]);

                i = closeBrace;
                headerStart = closeBrace + 1;
            }
            else if (ch == ';')
            {
                if (targetIndex >= headerStart && targetIndex <= i)
                    return ExtractMemberNameFromHeader(code[headerStart..i]);

                headerStart = i + 1;
            }
        }

        return null;
    }

    /// <summary>Index of a type's body <c>{</c>, skipping a primary-constructor parameter list; -1 when absent.</summary>
    private static int FindBodyOpenBrace(string code, SourceMask mask, int declarationStart, int end)
    {
        for (var i = declarationStart; i < end; i++)
        {
            if (!mask.IsCode(i)) continue;
            if (code[i] == '(')
            {
                var close = DeepSliceSyntax.FindMatchingParen(code, mask, i);
                if (close < i) return -1;
                i = close;
            }
            else if (code[i] == '{')
            {
                return i;
            }
            else if (code[i] == ';')
            {
                return -1; // body-less declaration: `record Foo(int X);`
            }
        }
        return -1;
    }

    private static string? ExtractMemberNameFromHeader(string rawHeader)
    {
        var header = rawHeader.Trim();
        if (header.Length == 0) return null;

        // Strip leading attributes [...]
        while (header.StartsWith('['))
        {
            var close = header.IndexOf(']');
            if (close < 0) break;
            header = header[(close + 1)..].TrimStart();
        }

        // If there is `=>` (expression-bodied member), keep only the signature before `=>`
        var arrow = header.IndexOf("=>", StringComparison.Ordinal);
        if (arrow > 0)
            header = header[..arrow].TrimEnd();

        // Method or constructor: take identifier before the first `(`
        var openParen = header.IndexOf('(');
        if (openParen > 0)
        {
            var beforeParen = header[..openParen].TrimEnd();
            var match = Regex.Match(beforeParen, @"(?<name>[A-Za-z_]\w*)\s*$");
            return match.Success ? match.Groups["name"].Value : null;
        }

        // Property: take the last identifier in the header
        var propMatch = Regex.Match(header, @"(?<name>[A-Za-z_]\w*)\s*$");
        return propMatch.Success ? propMatch.Groups["name"].Value : null;
    }

    private static bool ContainsObjectCreation(string code, int start, int length)
    {
        if (length <= 0) return false;
        var span = code.AsSpan(start, length);
        var index = 0;
        while (index < span.Length)
        {
            var relative = span[index..].IndexOf("new", StringComparison.Ordinal);
            if (relative < 0) return false;
            var at = index + relative;
            var previousIsWord = at > 0 &&
                (char.IsLetterOrDigit(span[at - 1]) || span[at - 1] == '_');
            var next = at + 3;
            if (!previousIsWord && next < span.Length && char.IsWhiteSpace(span[next]))
                return true;
            index = at + 3;
        }
        return false;
    }
}

internal sealed record DomainEventIndex(
    Dictionary<string, List<string>> RepoEntitiesByType,
    Dictionary<string, List<CqrsNotification>> WrapperNotificationsByInnerSimple,
    Dictionary<(string TypeName, string MemberName), List<string>> MemberEvents);
