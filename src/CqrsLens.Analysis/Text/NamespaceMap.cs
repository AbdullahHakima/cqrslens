using System.Text.RegularExpressions;

namespace CqrsLens;

/// <summary>
/// Namespace declarations with source spans. Supports file-scoped and nested
/// block-scoped namespaces (issue #15) while ignoring text inside literals/comments.
/// </summary>
internal sealed class NamespaceMap
{
    private static readonly Regex FileScopedRegex = new(
        @"^[ \t]*namespace\s+([\w.]+)\s*;", RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex BlockRegex = new(
        @"\bnamespace\s+([\w.]+)\s*\{", RegexOptions.Compiled);

    private readonly string _fileScoped;
    private readonly int _fileScopedIndex;
    private readonly List<(string Name, int Start, int End)> _blocks;

    private NamespaceMap(string fileScoped, int fileScopedIndex, List<(string Name, int Start, int End)> blocks)
    {
        _fileScoped = fileScoped;
        _fileScopedIndex = fileScopedIndex;
        _blocks = blocks;
    }

    public string FirstNamespace => _fileScoped.Length > 0
        ? _fileScoped
        : _blocks.Count > 0 ? _blocks[0].Name : "";

    public static NamespaceMap Build(string code) => Build(code, SourceMask.Build(code));

    public static NamespaceMap Build(string code, SourceMask mask)
    {
        var fileScoped = "";
        var fileScopedIndex = -1;
        foreach (Match m in FileScopedRegex.Matches(code))
        {
            if (!mask.IsCode(m.Index)) continue;
            fileScoped = m.Groups[1].Value;
            fileScopedIndex = m.Index;
            break;
        }

        var blocks = new List<(string Name, int Start, int End)>();
        foreach (Match m in BlockRegex.Matches(code))
        {
            if (!mask.IsCode(m.Index)) continue;
            var openBrace = m.Index + m.Length - 1;
            var closeBrace = mask.FindMatchingBrace(openBrace);
            blocks.Add((m.Groups[1].Value, m.Index, closeBrace < 0 ? code.Length : closeBrace + 1));
        }

        return new NamespaceMap(fileScoped, fileScopedIndex, blocks);
    }

    /// <summary>The innermost namespace that applies at <paramref name="sourceIndex"/>.</summary>
    public string NamespaceAt(int sourceIndex)
    {
        var parts = new List<string>();
        if (_fileScoped.Length > 0 && sourceIndex > _fileScopedIndex)
            parts.Add(_fileScoped);

        parts.AddRange(_blocks
            .Where(b => sourceIndex >= b.Start && sourceIndex < b.End)
            .OrderBy(b => b.Start)
            .Select(b => b.Name));

        return string.Join(".", parts);
    }
}
