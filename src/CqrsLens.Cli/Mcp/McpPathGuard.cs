namespace CqrsLens;

/// <summary>
/// Validates MCP-supplied file and directory paths against configured workspace roots,
/// blocking control characters, traversal segments, sibling-prefix bypasses, and symlink escapes.
/// </summary>
internal static class McpPathGuard
{
    private const int MaxSymlinkHops = 64;

    public static bool TryValidateDirectory(
        string rawPath,
        string workspaceRoot,
        IReadOnlyList<string> allowedRoots,
        bool allowExternalPaths,
        out string fullPath,
        out string? error)
    {
        if (!TryValidatePath(rawPath, workspaceRoot, allowedRoots, allowExternalPaths, out fullPath, out error))
            return false;

        if (!Directory.Exists(fullPath))
        {
            error = $"path not found: {rawPath}";
            fullPath = string.Empty;
            return false;
        }

        return true;
    }

    public static bool TryValidatePath(
        string rawPath,
        string workspaceRoot,
        IReadOnlyList<string> allowedRoots,
        bool allowExternalPaths,
        out string fullPath,
        out string? error)
    {
        fullPath = string.Empty;
        error = null;

        if (string.IsNullOrWhiteSpace(rawPath))
        {
            error = "path is required.";
            return false;
        }

        if (ContainsInvalidOrControlChars(rawPath))
        {
            error = "invalid path: control characters or invalid path characters are not allowed.";
            return false;
        }

        string? normalizedWorkspace = null;
        if (!string.IsNullOrWhiteSpace(workspaceRoot))
        {
            if (ContainsInvalidOrControlChars(workspaceRoot) ||
                !TryNormalizeFullPath(workspaceRoot, basePath: null, out normalizedWorkspace))
            {
                error = "invalid MCP workspace root.";
                return false;
            }
        }

        if (!TryNormalizeFullPath(rawPath, normalizedWorkspace, out var normalizedTarget))
        {
            error = $"invalid path: '{rawPath}'.";
            return false;
        }

        if (!TryResolveCanonicalPath(normalizedTarget, out var canonicalTarget, out error))
            return false;

        if (allowExternalPaths)
        {
            fullPath = canonicalTarget;
            return true;
        }

        var canonicalRoots = CollectCanonicalRoots(normalizedWorkspace, allowedRoots);
        if (canonicalRoots.Count == 0)
        {
            error = "path escapes allowed MCP workspace roots.";
            return false;
        }

        foreach (var root in canonicalRoots)
        {
            if (IsWithinRoot(root, canonicalTarget))
            {
                fullPath = canonicalTarget;
                return true;
            }
        }

        error = "path escapes allowed MCP workspace roots.";
        return false;
    }

    private static List<string> CollectCanonicalRoots(string? normalizedWorkspace, IReadOnlyList<string>? allowedRoots)
    {
        var canonicalRoots = new List<string>();
        if (normalizedWorkspace is not null &&
            TryResolveCanonicalPath(normalizedWorkspace, out var canonicalWorkspace, out _))
        {
            canonicalRoots.Add(canonicalWorkspace);
        }

        if (allowedRoots is null)
            return canonicalRoots;

        foreach (var allowed in allowedRoots)
        {
            if (string.IsNullOrWhiteSpace(allowed) || ContainsInvalidOrControlChars(allowed))
                continue;

            if (TryNormalizeFullPath(allowed, normalizedWorkspace, out var normalizedAllowed) &&
                TryResolveCanonicalPath(normalizedAllowed, out var canonicalAllowed, out _))
            {
                canonicalRoots.Add(canonicalAllowed);
            }
        }

        return canonicalRoots;
    }

    private static bool ContainsInvalidOrControlChars(string path)
    {
        var invalidChars = Path.GetInvalidPathChars();
        foreach (var c in path)
        {
            if (c == '\0' || c < 0x20 || Array.IndexOf(invalidChars, c) >= 0)
                return true;
        }

        return false;
    }

    private static bool TryNormalizeFullPath(string path, string? basePath, out string fullPath)
    {
        try
        {
            fullPath = Path.IsPathFullyQualified(path) || string.IsNullOrWhiteSpace(basePath)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(path, basePath);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            fullPath = string.Empty;
            return false;
        }
    }

    private static bool TryResolveCanonicalPath(string normalizedPath, out string canonicalPath, out string? error)
    {
        canonicalPath = string.Empty;
        error = null;

        var current = Path.TrimEndingDirectorySeparator(normalizedPath);
        for (var hop = 0; hop < MaxSymlinkHops; hop++)
        {
            var root = Path.GetPathRoot(current);
            if (string.IsNullOrEmpty(root))
            {
                canonicalPath = current;
                return true;
            }

            var remainder = current[root.Length..];
            var segments = remainder.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries);

            var prefix = root;
            var resolvedRedirect = false;

            for (var i = 0; i < segments.Length; i++)
            {
                prefix = Path.Combine(prefix, segments[i]);

                if (!TryGetSymlinkTarget(prefix, out var target, out error))
                    return false;

                if (target is null)
                    continue;

                var rebuilt = target;
                for (var j = i + 1; j < segments.Length; j++)
                    rebuilt = Path.Combine(rebuilt, segments[j]);

                if (!TryNormalizeFullPath(rebuilt, basePath: null, out var normalizedRebuilt))
                {
                    error = $"invalid symbolic link target: '{rebuilt}'.";
                    return false;
                }

                current = Path.TrimEndingDirectorySeparator(normalizedRebuilt);
                resolvedRedirect = true;
                break;
            }

            if (!resolvedRedirect)
            {
                canonicalPath = current;
                return true;
            }
        }

        error = "path exceeds maximum symbolic link resolution depth.";
        return false;
    }

    private static bool TryGetSymlinkTarget(string candidatePath, out string? target, out string? error)
    {
        target = null;
        error = null;

        try
        {
            var fileInfo = new FileInfo(candidatePath);
            if (fileInfo.LinkTarget is not { } rawTarget)
                return true;

            FileSystemInfo? resolved = Directory.Exists(candidatePath)
                ? Directory.ResolveLinkTarget(candidatePath, returnFinalTarget: true)
                : File.Exists(candidatePath)
                    ? File.ResolveLinkTarget(candidatePath, returnFinalTarget: true)
                    : null;

            if (resolved is not null)
            {
                target = resolved.FullName;
                return true;
            }

            var parent = Path.GetDirectoryName(candidatePath) ?? Path.GetPathRoot(candidatePath) ?? string.Empty;
            target = Path.IsPathFullyQualified(rawTarget) || string.IsNullOrEmpty(parent)
                ? Path.GetFullPath(rawTarget)
                : Path.GetFullPath(rawTarget, parent);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = $"failed to resolve path '{candidatePath}': {ex.Message}";
            return false;
        }
    }

    private static bool IsWithinRoot(string canonicalRoot, string canonicalTarget)
    {
        var relative = Path.GetRelativePath(canonicalRoot, canonicalTarget);
        if (string.Equals(relative, ".", StringComparison.Ordinal))
            return true;

        if (Path.IsPathRooted(relative))
            return false;

        if (string.Equals(relative, "..", StringComparison.Ordinal) ||
            relative.StartsWith("../", StringComparison.Ordinal) ||
            relative.StartsWith("..\\", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }
}
