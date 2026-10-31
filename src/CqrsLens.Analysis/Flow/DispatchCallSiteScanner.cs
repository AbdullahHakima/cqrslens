using System.Text.RegularExpressions;

namespace CqrsLens;

/// <summary>
/// Finds dispatch call sites inside one file and resolves their target type. A call
/// site is only turned into an edge when the target type is statically resolved to an
/// inventoried request/notification; a runtime-typed, cast or otherwise unresolvable
/// target is recorded as an unresolved dispatch instead of being guessed.
/// </summary>
internal sealed partial class DispatchCallSiteScanner
{
    [GeneratedRegex(@"\.\s*(?<method>Send|SendAsync|Publish|PublishAsync|InvokeAsync)\s*(?:<\s*(?<generic>(?:global::)?[A-Za-z_]\w*(?:\s*\.\s*[A-Za-z_]\w*)*)\s*(?:,|>))?\s*\(")]
    private static partial Regex DispatchCallRegex();

    [GeneratedRegex(@"^new\s+(?<type>(?:global::)?[A-Za-z_]\w*(?:\s*\.\s*[A-Za-z_]\w*)*)\s*[\(\{<]")]
    private static partial Regex LeadingObjectCreationRegex();

    [GeneratedRegex(@"^@?[A-Za-z_]\w*(?:\s*\.\s*@?[A-Za-z_]\w*)*$")]
    private static partial Regex SimpleIdentifierRegex();

    private static readonly Dictionary<string, string> EmptyStrings = new(StringComparer.Ordinal);
    private static readonly HashSet<string> EmptySet = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, List<string>> EmptyLists = new(StringComparer.Ordinal);

    private readonly AnalysisResult _result;
    private readonly LinkIndex<CqrsRequest> _requests;
    private readonly LinkIndex<CqrsNotification> _notifications;
    private readonly HashSet<string> _declaredClasses;
    private readonly HashSet<string> _handlerNames;
    private readonly HashSet<string> _notificationHandlerNames;
    private readonly HashSet<string> _testProjects;

    internal DispatchCallSiteScanner(
        AnalysisResult result,
        LinkIndex<CqrsRequest> requests,
        LinkIndex<CqrsNotification> notifications,
        HashSet<string> declaredClasses,
        HashSet<string> handlerNames,
        HashSet<string> notificationHandlerNames,
        HashSet<string> testProjects)
    {
        _result = result;
        _requests = requests;
        _notifications = notifications;
        _declaredClasses = declaredClasses;
        _handlerNames = handlerNames;
        _notificationHandlerNames = notificationHandlerNames;
        _testProjects = testProjects;
    }

    internal void ScanFile(SourceFile file, CSharpScanner.FileScan scan, SourceMask mask)
    {
        foreach (var decl in scan.Classes)
        {
            if (decl.BodySpan.End <= decl.BodySpan.Start)
                continue;

            var bodyEnd = Math.Min(file.Code.Length, decl.BodySpan.End);
            Dictionary<string, string>? receiverTypes = null;
            List<string>? imports = null;

            foreach (Match match in DispatchCallRegex().Matches(file.Code, decl.BodySpan.Start))
            {
                if (match.Index >= bodyEnd)
                    break;
                if (!mask.IsCode(match.Index))
                    continue;
                if (!ReferenceEquals(DeepSliceSyntax.InnermostClass(scan.Classes, match.Index), decl))
                    continue;

                receiverTypes ??= ReceiverTypeMapBuilder.Build(
                    decl, file.Code, mask, scan.Classes, EmptyStrings, EmptySet, EmptyLists);
                imports ??= BuildImports(decl);

                Process(match, decl, file, mask, receiverTypes, imports);
            }
        }
    }

    private void Process(
        Match match,
        CSharpScanner.ClassDecl decl,
        SourceFile file,
        SourceMask mask,
        Dictionary<string, string> receiverTypes,
        List<string> imports)
    {
        var method = match.Groups["method"].Value;
        var publishLike = method is "Publish" or "PublishAsync";
        var receiver = ReceiverTypeMapBuilder.GetReceiverIdentifier(file.Code, match.Index);
        var isTest = file.ProjectName is not null && _testProjects.Contains(file.ProjectName);
        var location = SourceLocation.Create(file.Path, _result.RootPath, file.Code, match.Index);

        string? typeName;
        if (match.Groups["generic"].Success && match.Groups["generic"].Value.Length > 0)
        {
            typeName = match.Groups["generic"].Value;
        }
        else
        {
            var argument = ExtractFirstArgument(file.Code, mask, match.Index + match.Length);
            typeName = TypeFromArgument(argument, receiverTypes);
        }

        // Publish targets a notification; Send/SendAsync/InvokeAsync target a request
        // but a few frameworks dispatch an event through the same method, so a
        // non-publish call falls back to a notification target.
        if (TryResolve(typeName, decl, imports, file.ProjectName, publishLike, out var target, out var kind)
            && target is not null)
        {
            Record(decl, file, isTest, location, target, kind);
            return;
        }

        if (typeName is not null &&
            _declaredClasses.Contains(CSharpScanner.UnqualifiedTypeName(typeName)))
        {
            // The target was written as a concrete type that is simply not an
            // inventoried request/notification (an integration event, a wrapper, a
            // DTO). That is a resolved dispatch to a non-CQRS type, not an
            // unresolved one: no edge, and no unresolved accounting.
            return;
        }

        if (IsDispatchReceiver(receiver))
            RecordUnresolved(method, file, isTest, location, receiver);
    }

    private bool TryResolve(
        string? typeName,
        CSharpScanner.ClassDecl decl,
        List<string> imports,
        string? projectName,
        bool publishLike,
        out string? target,
        out DispatchTargetKind kind)
    {
        target = null;
        kind = DispatchTargetKind.Request;
        if (typeName is null)
            return false;

        var simple = CSharpScanner.UnqualifiedTypeName(typeName);
        var writtenNamespace = CSharpScanner.NamespaceOfType(typeName);

        if (!publishLike)
        {
            var request = _requests.Resolve(simple, writtenNamespace, decl.Namespace, imports, projectName);
            if (request is not null)
            {
                target = request.FullName;
                kind = DispatchTargetKind.Request;
                return true;
            }
        }

        var notification = _notifications.Resolve(simple, writtenNamespace, decl.Namespace, imports, projectName);
        if (notification is not null)
        {
            target = notification.FullName;
            kind = DispatchTargetKind.Notification;
            return true;
        }

        return false;
    }

    private void Record(
        CSharpScanner.ClassDecl decl,
        SourceFile file,
        bool isTest,
        SourceLocation location,
        string targetFullName,
        DispatchTargetKind kind)
    {
        _result.DispatchCallSites.Add(new DispatchCallSite
        {
            TargetFullName = targetFullName,
            TargetKind = kind,
            SenderFullName = decl.FullName,
            ProjectName = file.ProjectName,
            IsTestProject = isTest,
            Location = location,
        });

        var kindValue = kind == DispatchTargetKind.Request
            ? FlowEdgeKind.HandlerToRequest
            : FlowEdgeKind.HandlerToNotification;
        if (_handlerNames.Contains(decl.FullName) || _notificationHandlerNames.Contains(decl.FullName))
        {
            _result.FlowEdges.Add(new FlowEdge
            {
                Kind = kindValue,
                From = decl.FullName,
                To = targetFullName,
                ProjectName = file.ProjectName,
                Location = location,
            });
        }
    }

    private void RecordUnresolved(
        string method,
        SourceFile file,
        bool isTest,
        SourceLocation location,
        string? receiver)
    {
        _result.UnresolvedDispatches.Add(new UnresolvedDispatch
        {
            Method = method,
            ReceiverName = receiver,
            ProjectName = file.ProjectName,
            IsTestProject = isTest,
            Location = location,
        });
    }

    private static string? TypeFromArgument(string argument, Dictionary<string, string> receiverTypes)
    {
        var arg = argument.Trim();
        if (arg.Length == 0)
            return null;

        var created = LeadingObjectCreationRegex().Match(arg);
        if (created.Success)
            return created.Groups["type"].Value;

        if (SimpleIdentifierRegex().IsMatch(arg))
        {
            var name = arg.TrimStart('@');
            return receiverTypes.TryGetValue(name, out var declared) && declared.Length > 0
                ? declared
                : null;
        }

        return null;
    }

    private static string ExtractFirstArgument(string code, SourceMask mask, int start)
    {
        var depth = 0;
        var index = start;
        for (; index < code.Length; index++)
        {
            if (!mask.IsCode(index))
                continue;

            var c = code[index];
            if (c is '(' or '[' or '{')
            {
                depth++;
            }
            else if (c is ')' or ']' or '}')
            {
                if (c == ')' && depth == 0)
                    break;
                depth--;
            }
            else if (depth == 0 && (c is ',' or ';'))
            {
                break;
            }
        }

        return index > start ? code[start..index] : "";
    }

    private static List<string> BuildImports(CSharpScanner.ClassDecl decl)
    {
        var imports = new List<string>();
        CSharpScanner.AddImportedNamespaces(decl.Usings, imports);
        return imports;
    }

    private static bool IsDispatchReceiver(string? receiver)
    {
        if (receiver is null or "this" or "base")
            return false;

        var value = receiver.TrimStart('_').ToLowerInvariant();
        return value.Contains("mediator", StringComparison.Ordinal) ||
               value.Contains("sender", StringComparison.Ordinal) ||
               value.Contains("publisher", StringComparison.Ordinal) ||
               value.Contains("bus", StringComparison.Ordinal) ||
               value.Contains("dispatch", StringComparison.Ordinal) ||
               value.Contains("broker", StringComparison.Ordinal);
    }
}
