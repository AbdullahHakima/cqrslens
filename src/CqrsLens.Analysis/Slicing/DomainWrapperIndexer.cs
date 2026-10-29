namespace CqrsLens;

/// <summary>
/// Builds the notification-wrapper lookup: a generic wrapper such as
/// <c>FooNotification : DomainNotificationBase&lt;FooDomainEvent&gt;</c> is attributed to the
/// notification it wraps.
/// </summary>
internal static class DomainWrapperIndexer
{
    internal static void Index(
        List<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles,
        Dictionary<string, List<CqrsNotification>> notificationsBySimple,
        DomainEventIndexCollector collector)
    {
        foreach (var parsed in parsedFiles)
        {
            foreach (var decl in parsed.Scan.Classes)
            {
                if (!notificationsBySimple.TryGetValue(decl.Name, out var selfNotifications))
                    continue;

                var matchingSelf = selfNotifications.FirstOrDefault(
                    n => string.Equals(n.FullName, decl.FullName, StringComparison.Ordinal));
                if (matchingSelf is null)
                    continue;

                foreach (var baseType in CSharpScanner.SplitTopLevel(decl.BaseList))
                {
                    var open = baseType.IndexOf('<');
                    var close = baseType.LastIndexOf('>');
                    if (open <= 0 || close <= open + 1)
                        continue;

                    var innerSimple = CSharpScanner.SimpleName(baseType[(open + 1)..close].Trim());
                    if (notificationsBySimple.ContainsKey(innerSimple))
                        collector.AddWrapper(innerSimple, matchingSelf);
                }
            }
        }
    }
}
