using System.Text.RegularExpressions;

namespace CqrsLens;

/// <summary>
/// A root-relative glob used by .cqrslens.json. Patterns use '/' separators;
/// '**' crosses directories, '*' stays within one segment and '?' matches one
/// character. Matching is case-insensitive so configs behave the same on
/// Windows and Linux.
/// </summary>
public sealed class GlobPattern
{
    private readonly Regex _regex;

    public GlobPattern(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            throw new ArgumentException("Glob patterns cannot be empty.", nameof(pattern));

        Pattern = Normalize(pattern);
        _regex = new Regex(
            ToRegex(Pattern),
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
            TimeSpan.FromSeconds(1));
    }

    public string Pattern { get; }

    public bool IsMatch(string relativePath)
        => _regex.IsMatch(Normalize(relativePath));

    private static string Normalize(string value)
        => value.Replace('\\', '/').Trim().TrimStart('/');

    private static string ToRegex(string pattern)
    {
        var regex = new System.Text.StringBuilder("^");
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c == '*')
            {
                if (i + 1 < pattern.Length && pattern[i + 1] == '*')
                {
                    i++;
                    if (i + 1 < pattern.Length && pattern[i + 1] == '/')
                    {
                        i++;
                        regex.Append("(?:.*/)?");
                    }
                    else
                    {
                        regex.Append(".*");
                    }
                }
                else
                {
                    regex.Append("[^/]*");
                }
            }
            else if (c == '?')
            {
                regex.Append("[^/]");
            }
            else
            {
                regex.Append(Regex.Escape(c.ToString()));
            }
        }

        return regex.Append('$').ToString();
    }
}

/// <summary>Include/exclude filtering for source paths relative to the scan root.</summary>
public sealed class SourcePathFilter
{
    private readonly IReadOnlyList<GlobPattern> _includes;
    private readonly IReadOnlyList<GlobPattern> _excludes;

    public SourcePathFilter(
        IEnumerable<string>? includes = null,
        IEnumerable<string>? excludes = null)
    {
        _includes = (includes ?? Array.Empty<string>())
            .Select(p => new GlobPattern(p))
            .ToArray();
        _excludes = (excludes ?? Array.Empty<string>())
            .Select(p => new GlobPattern(p))
            .ToArray();
    }

    public bool IsIncluded(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        if (_includes.Count > 0 && !_includes.Any(p => p.IsMatch(normalized)))
            return false;
        return !_excludes.Any(p => p.IsMatch(normalized));
    }
}
