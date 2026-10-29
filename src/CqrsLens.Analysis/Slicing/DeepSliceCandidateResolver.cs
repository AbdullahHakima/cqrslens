using System.Text.RegularExpressions;

namespace CqrsLens;

/// <summary>
/// Fail-closed resolution of same-named CQRS declarations. A candidate is only returned when
/// imports, namespace or project narrow the set to exactly one; otherwise the caller gets null
/// instead of an arbitrary first match.
/// </summary>
internal static class DeepSliceCandidateResolver
{
    internal static CqrsRequest? PickRequest(
        List<CqrsRequest> candidates,
        string? projectName,
        IReadOnlyList<UsingDirective>? usings)
    {
        if (candidates.Count == 1) return candidates[0];

        if (usings is { Count: > 0 })
        {
            var importedNs = new HashSet<string>(
                usings.SelectMany(u => u.NamespaceCandidates()),
                StringComparer.Ordinal);
            var byUsing = candidates
                .Where(c => !string.IsNullOrEmpty(c.Namespace) && importedNs.Contains(c.Namespace))
                .ToList();
            if (byUsing.Count == 1)
                return byUsing[0];
        }

        if (!string.IsNullOrEmpty(projectName))
        {
            var sameProj = candidates
                .Where(c => string.Equals(c.ProjectName, projectName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (sameProj.Count == 1) return sameProj[0];
        }

        // Still ambiguous (e.g. the same command name in two modules): report nothing rather than
        // attributing the reference to whichever declaration happened to be scanned first.
        return null;
    }

    internal static HandlerInfo? PickHandler(List<HandlerInfo> candidates, string? projectName)
    {
        if (candidates.Count == 1) return candidates[0];
        if (!string.IsNullOrEmpty(projectName))
        {
            var sameProj = candidates
                .Where(c => string.Equals(c.ProjectName, projectName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (sameProj.Count == 1) return sameProj[0];
        }

        // Same handler name in two modules: do not attribute anything to an arbitrary one.
        return null;
    }

    internal static NotificationHandlerInfo? PickNotificationHandler(
        List<NotificationHandlerInfo> candidates,
        string? projectName)
    {
        if (candidates.Count == 1) return candidates[0];
        if (!string.IsNullOrEmpty(projectName))
        {
            var sameProj = candidates
                .Where(c => string.Equals(c.ProjectName, projectName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (sameProj.Count == 1) return sameProj[0];
        }

        // Same handler name in two modules: do not attribute anything to an arbitrary one.
        return null;
    }

    internal static CqrsNotification? PickNotification(
        List<CqrsNotification> candidates,
        string? callerNamespace,
        string? projectName)
    {
        if (candidates.Count == 1) return candidates[0];
        if (!string.IsNullOrEmpty(callerNamespace))
        {
            var sameNs = candidates
                .Where(c => string.Equals(c.Namespace, callerNamespace, StringComparison.Ordinal))
                .ToList();
            if (sameNs.Count == 1) return sameNs[0];
        }
        if (!string.IsNullOrEmpty(projectName))
        {
            var sameProj = candidates
                .Where(c => string.Equals(c.ProjectName, projectName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (sameProj.Count == 1) return sameProj[0];
        }

        // Ambiguous across modules: do not guess which event was meant.
        return null;
    }
}
