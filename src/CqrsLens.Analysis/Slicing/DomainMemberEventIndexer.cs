using System.Text.RegularExpressions;
using static CqrsLens.DeepSlicePatterns;

namespace CqrsLens;

/// <summary>
/// Records notifications raised directly by domain members, then propagates events from types
/// instantiated or called by those members so factories and constructors are attributed across
/// two hops.
/// </summary>
internal static class DomainMemberEventIndexer
{
    internal static void Index(
        List<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles,
        Dictionary<string, List<CqrsNotification>> notificationsBySimple,
        Dictionary<string, List<HandlerInfo>> handlersByFull,
        Dictionary<string, List<NotificationHandlerInfo>> notifHandlersByFull,
        DomainEventIndexCollector collector)
    {
        var classesToPropagate =
            new List<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask, CSharpScanner.ClassDecl Decl)>();

        foreach (var (file, scan, mask) in parsedFiles)
        {
            foreach (var decl in scan.Classes)
            {
                if (handlersByFull.ContainsKey(decl.FullName) ||
                    notifHandlersByFull.ContainsKey(decl.FullName))
                    continue;

                if (decl.BodySpan.End <= decl.BodySpan.Start)
                    continue;

                var bodyStart = decl.BodySpan.Start;
                var bodyEnd = Math.Min(file.Code.Length, decl.BodySpan.End);
                var nonGenericInterfaces = ReceiverTypeMapBuilder.NonGenericInterfaceNames(decl);
                var hasDirectOrFactoryCandidate = false;

                if (ContainsObjectCreation(file.Code, bodyStart, bodyEnd - bodyStart))
                {
                    foreach (Match match in ObjectCreationRegex().Matches(file.Code, bodyStart))
                    {
                        if (match.Index >= bodyEnd) break;
                        if (!mask.IsCode(match.Index)) continue;
                        if (!ReferenceEquals(
                                DeepSliceSyntax.InnermostClass(scan.Classes, match.Index), decl))
                            continue;

                        var createdSimple = CSharpScanner.UnqualifiedTypeName(match.Groups["type"].Value);
                        if (notificationsBySimple.ContainsKey(createdSimple))
                        {
                            var memberName = EnclosingMemberLocator.Find(file.Code, mask, decl, match.Index);
                            if (!string.IsNullOrEmpty(memberName))
                            {
                                collector.AddMemberEvent(decl.Name, memberName, createdSimple);
                                foreach (var iface in nonGenericInterfaces)
                                    collector.AddMemberEvent(iface, memberName, createdSimple);
                            }
                        }
                        else
                        {
                            hasDirectOrFactoryCandidate = true;
                        }
                    }
                }

                if (hasDirectOrFactoryCandidate)
                    classesToPropagate.Add((file, scan, mask, decl));
            }
        }

        // Propagate events from domain constructors and static factories (2 hops, e.g.
        // new MeetingGroupProposal -> ProposeNew, and
        // new MeetingGroup -> CreateBasedOnProposal -> CreateMeetingGroup).
        for (var hop = 0; hop < 2 && collector.MemberEvents.Count > 0; hop++)
        {
            foreach (var (file, scan, mask, decl) in classesToPropagate)
            {
                var bodyStart = decl.BodySpan.Start;
                var bodyEnd = Math.Min(file.Code.Length, decl.BodySpan.End);

                // Propagate `new TargetClass(...)` constructor events to enclosing factory methods.
                foreach (Match match in ObjectCreationRegex().Matches(file.Code, bodyStart))
                {
                    if (match.Index >= bodyEnd) break;
                    if (!mask.IsCode(match.Index)) continue;
                    if (!ReferenceEquals(
                            DeepSliceSyntax.InnermostClass(scan.Classes, match.Index), decl))
                        continue;

                    var targetSimple = CSharpScanner.UnqualifiedTypeName(match.Groups["type"].Value);
                    if (!collector.MemberEvents.TryGetValue(
                            (targetSimple, targetSimple), out var constructorEvents))
                        continue;

                    var callerMember = EnclosingMemberLocator.Find(file.Code, mask, decl, match.Index);
                    if (string.IsNullOrEmpty(callerMember))
                        continue;

                    foreach (var eventSimpleName in constructorEvents.ToList())
                        collector.AddMemberEvent(decl.Name, callerMember, eventSimpleName);
                }

                // Propagate `TargetClass.StaticFactory(...)` events to enclosing methods.
                foreach (Match match in StaticMethodCallRegex().Matches(file.Code, bodyStart))
                {
                    if (match.Index >= bodyEnd) break;
                    if (!mask.IsCode(match.Index)) continue;
                    if (!ReferenceEquals(
                            DeepSliceSyntax.InnermostClass(scan.Classes, match.Index), decl))
                        continue;

                    var targetType = match.Groups["type"].Value;
                    var targetMethod = match.Groups["method"].Value;
                    if (!collector.MemberEvents.TryGetValue((targetType, targetMethod), out var staticEvents))
                        continue;

                    var callerMember = EnclosingMemberLocator.Find(file.Code, mask, decl, match.Index);
                    if (string.IsNullOrEmpty(callerMember))
                        continue;

                    foreach (var eventSimpleName in staticEvents.ToList())
                        collector.AddMemberEvent(decl.Name, callerMember, eventSimpleName);
                }
            }
        }
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
