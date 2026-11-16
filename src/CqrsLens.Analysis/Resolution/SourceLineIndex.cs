namespace CqrsLens;

/// <summary>
/// Per-file O(log lines) offset-to-<see cref="SourceLocation"/> converter with cached
/// scan-relative path, avoiding repeated linear newline scans and path normalization.
/// </summary>
internal sealed class SourceLineIndex
{
    private readonly string _code;
    private readonly string _relativePath;
    private int[]? _lineStarts;

    public string RelativePath => _relativePath;

    public SourceLineIndex(SourceFile file, string rootPath)
    {
        _code = file.Code;
        _relativePath = Path.GetRelativePath(rootPath, file.Path)
            .Replace(Path.DirectorySeparatorChar, '/');
    }

    public static Dictionary<string, SourceLineIndex> BuildLookup(
        IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles,
        string rootPath)
    {
        var map = new Dictionary<string, SourceLineIndex>(parsedFiles.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (file, _, _) in parsedFiles)
            map.TryAdd(file.Path, new SourceLineIndex(file, rootPath));
        return map;
    }

    public SourceLocation? Create(int index)
    {
        if (index < 0 || index > _code.Length || string.IsNullOrEmpty(_relativePath))
            return null;

        if (index == 0)
            return new SourceLocation(_relativePath, 1, 1);

        var lineStarts = _lineStarts ??= ComputeLineStarts(_code);
        var search = Array.BinarySearch(lineStarts, index);
        if (search >= 0)
            return new SourceLocation(_relativePath, search + 1, 1);

        var insertion = ~search;
        var lineIndex = insertion - 1;
        return new SourceLocation(_relativePath, lineIndex + 1, index - lineStarts[lineIndex] + 1);
    }

    private static int[] ComputeLineStarts(string code)
    {
        var starts = new List<int>(Math.Max(16, code.Length / 40)) { 0 };
        var pos = 0;
        while (pos < code.Length)
        {
            var nl = code.IndexOf('\n', pos);
            if (nl < 0)
                break;
            starts.Add(nl + 1);
            pos = nl + 1;
        }
        return starts.ToArray();
    }
}
