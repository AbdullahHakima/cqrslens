namespace CqrsLens;

/// <summary>
/// Shared identity sets for one scan run. Deduplicating with lists made shape
/// discovery quadratic on large solutions (every class re-walked every shape);
/// these sets keep it O(1) per declaration without changing output order.
/// </summary>
internal sealed class ScanIdentityIndex
{
    private readonly HashSet<string> _requests;
    private readonly HashSet<string> _notifications;
    private readonly HashSet<string> _handlers;
    private readonly HashSet<string> _notificationHandlers;
    private readonly HashSet<string> _validators;

    public ScanIdentityIndex(AnalysisResult result)
    {
        _requests = new HashSet<string>(
            result.Requests.Select(r => TypeKey(r.Namespace, r.ContainingTypes, r.Name, r.ProjectName)),
            StringComparer.Ordinal);
        _notifications = new HashSet<string>(
            result.Notifications.Select(n => TypeKey(n.Namespace, n.ContainingTypes, n.Name, n.ProjectName)),
            StringComparer.Ordinal);
        _handlers = new HashSet<string>(
            result.Handlers.Select(h => TypeKey(h.Namespace, h.ContainingTypes, h.Name, h.ProjectName) + "\u001f" + h.RequestName),
            StringComparer.Ordinal);
        _notificationHandlers = new HashSet<string>(
            result.NotificationHandlers.Select(h => TypeKey(h.Namespace, h.ContainingTypes, h.Name, h.ProjectName) + "\u001f" + h.NotificationName),
            StringComparer.Ordinal);
        _validators = new HashSet<string>(
            result.Validators.Select(v => TypeKey(v.Namespace, v.ContainingTypes, v.Name, v.ProjectName) + "\u001f" + v.TargetType),
            StringComparer.Ordinal);
    }

    public bool TryAddRequest(CSharpScanner.ClassDecl decl, string? projectName)
        => _requests.Add(TypeKey(decl.Namespace, decl.ContainingTypes, decl.Name, projectName));

    public bool TryAddNotification(CSharpScanner.ClassDecl decl, string? projectName)
        => _notifications.Add(TypeKey(decl.Namespace, decl.ContainingTypes, decl.Name, projectName));

    public bool TryAddHandler(CSharpScanner.ClassDecl decl, string? projectName, string requestName)
        => _handlers.Add(TypeKey(decl.Namespace, decl.ContainingTypes, decl.Name, projectName) + "\u001f" + requestName);

    public bool TryAddNotificationHandler(CSharpScanner.ClassDecl decl, string? projectName, string notificationName)
        => _notificationHandlers.Add(TypeKey(decl.Namespace, decl.ContainingTypes, decl.Name, projectName) + "\u001f" + notificationName);

    public bool TryAddValidator(CSharpScanner.ClassDecl decl, string? projectName, string targetType)
        => _validators.Add(TypeKey(decl.Namespace, decl.ContainingTypes, decl.Name, projectName) + "\u001f" + targetType);

    private static string TypeKey(
        string ns,
        IReadOnlyList<string> containingTypes,
        string name,
        string? projectName)
        => $"{projectName ?? ""}\u001f{ns}\u001f{string.Join(".", containingTypes)}\u001f{name}";
}
