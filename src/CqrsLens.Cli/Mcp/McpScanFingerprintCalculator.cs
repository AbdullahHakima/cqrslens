namespace CqrsLens;

/// <summary>
/// Cache invalidation must notice added/removed/renamed inputs as well as edits
/// whose timestamp is older than the newest file. Hash every source, project,
/// build-config, solution, explicit config and baseline file name with its size
/// and write time.
/// </summary>
internal static class McpScanFingerprintCalculator
{
    public static McpScanFingerprint Calculate(
        string fullRoot,
        string? configPath,
        string? baselinePath)
    {
        var fileCount = 0;
        var maxTicks = 0L;
        var hash = 14695981039346656037UL;

        void AddHash(ulong value)
        {
            for (var i = 0; i < 8; i++)
            {
                hash ^= (byte)(value >> (i * 8));
                hash *= 1099511628211UL;
            }
        }

        void AddString(string value)
        {
            foreach (var character in value)
            {
                hash ^= character;
                hash *= 1099511628211UL;
            }

            AddHash(0xFFFF); // separator so "ab"+"c" != "a"+"bc"
        }

        void Include(string path)
        {
            try
            {
                var info = new FileInfo(Path.GetFullPath(path));
                if (!info.Exists)
                    return;

                fileCount++;
                var ticks = info.LastWriteTimeUtc.Ticks;
                if (ticks > maxTicks)
                    maxTicks = ticks;

                AddString(Path.GetRelativePath(fullRoot, info.FullName));
                AddHash((ulong)info.Length);
                AddHash((ulong)ticks);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // Unreadable inputs simply do not participate in the fingerprint.
            }
        }

        try
        {
            foreach (var file in CsprojParser.FindSourceFiles(fullRoot))
                Include(file);
            foreach (var file in CsprojParser.FindProjectFiles(fullRoot))
                Include(file);

            var discoveryOptions = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
                ReturnSpecialDirectories = false,
            };

            foreach (var name in new[] { "Directory.Build.props", "Directory.Build.targets", ".cqrslens.json" })
            {
                foreach (var file in Directory.EnumerateFiles(fullRoot, name, discoveryOptions))
                {
                    if (!CsprojParser.IsSkippedPath(file, fullRoot))
                        Include(file);
                }
            }

            foreach (var file in Directory.EnumerateFiles(fullRoot, "*.sln", discoveryOptions))
            {
                if (!CsprojParser.IsSkippedPath(file, fullRoot))
                    Include(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Fall back to whatever was included before enumeration failed.
        }

        if (!string.IsNullOrWhiteSpace(configPath))
            Include(configPath);
        if (!string.IsNullOrWhiteSpace(baselinePath))
            Include(baselinePath);

        return new McpScanFingerprint(fileCount, maxTicks, hash);
    }
}
