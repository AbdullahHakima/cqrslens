namespace CqrsLens;

/// <summary>Writes an output file, creating its parent directory when needed.</summary>
internal static class OutputFileWriter
{
    public static void Write(string fullPath, string content)
    {
        EnsureDirectory(fullPath);
        File.WriteAllText(fullPath, content);
    }

    public static void EnsureDirectory(string fullPath)
    {
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
    }
}
