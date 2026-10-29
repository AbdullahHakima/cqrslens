namespace CqrsLens;

/// <summary>A 1-based source location. File paths are relative to the scan root when possible.</summary>
public sealed record SourceLocation(string File, int Line, int Column)
{
    public string FilePath => File;

    public override string ToString() => $"{File}:{Line}:{Column}";

    public static SourceLocation? Create(
        string? filePath,
        string rootPath,
        string code,
        int index)
    {
        if (string.IsNullOrEmpty(filePath) || index < 0 || index > code.Length)
            return null;

        var line = 1;
        var lineStart = 0;
        for (var i = 0; i < index; i++)
        {
            if (code[i] != '\n') continue;
            line++;
            lineStart = i + 1;
        }

        return new SourceLocation(
            RelativeFile(filePath, rootPath),
            line,
            index - lineStart + 1);
    }

    public static SourceLocation? FromLine(
        string? filePath,
        string rootPath,
        int line,
        int column)
    {
        if (string.IsNullOrEmpty(filePath))
            return null;

        return new SourceLocation(
            RelativeFile(filePath, rootPath),
            Math.Max(1, line),
            Math.Max(1, column));
    }

    private static string RelativeFile(string filePath, string rootPath)
    {
        var relative = Path.GetRelativePath(rootPath, filePath)
            .Replace(Path.DirectorySeparatorChar, '/');
        return relative;
    }
}
