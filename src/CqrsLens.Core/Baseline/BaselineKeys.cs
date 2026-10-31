namespace CqrsLens;

/// <summary>
/// Stable identity used to match a finding against a baseline entry. Line
/// numbers deliberately do not participate, so moving code does not turn a
/// known finding into a new one.
/// </summary>
public static class BaselineKeys
{
    public static string Create(string code, string? target, string? file)
    {
        var normalizedFile = (file ?? "")
            .Replace('\\', '/')
            .Trim()
            .TrimStart('/')
            .ToLowerInvariant();
        return $"{code}\u001f{target ?? ""}\u001f{normalizedFile}";
    }
}
