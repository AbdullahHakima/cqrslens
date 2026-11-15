namespace CqrsLens;

/// <summary>
/// Stable identity of a scanned project: the normalized root-relative path of its <c>.csproj</c> file.
/// Unique within a scan by construction (F0 §2).
/// </summary>
public sealed record ProjectId : IComparable<ProjectId>
{
    private readonly int _hashCode;
    private NodeReference? _nodeReference;

    public string Value { get; }

    private ProjectId(string value)
    {
        Value = value;
        _hashCode = StringComparer.Ordinal.GetHashCode(value);
    }

    internal NodeReference AsNodeReference() => _nodeReference ??= NodeReference.CreateProject(this);

    public static ProjectId FromRelativePath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        var normalized = Normalize(relativePath);
        if (normalized.Length == 0)
            throw new ArgumentException("Project path cannot be empty after normalization.", nameof(relativePath));
        return new ProjectId(normalized);
    }

    public static ProjectId FromProjectFile(string projectFilePath, string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        var relative = Path.GetRelativePath(rootPath, projectFilePath);
        return FromRelativePath(relative);
    }

    public bool Equals(ProjectId? other)
        => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override int GetHashCode() => _hashCode;

    public int CompareTo(ProjectId? other)
        => other is null ? 1 : string.Compare(Value, other.Value, StringComparison.Ordinal);

    public static bool operator <(ProjectId? left, ProjectId? right)
        => left is null ? right is not null : left.CompareTo(right) < 0;

    public static bool operator <=(ProjectId? left, ProjectId? right)
        => left is null || left.CompareTo(right) <= 0;

    public static bool operator >(ProjectId? left, ProjectId? right)
        => left is not null && left.CompareTo(right) > 0;

    public static bool operator >=(ProjectId? left, ProjectId? right)
        => left is null ? right is null : left.CompareTo(right) >= 0;

    public override string ToString() => Value;

    private static string Normalize(string path)
    {
        var normalized = path.Trim();
        if (normalized.Contains('\\', StringComparison.Ordinal))
            normalized = normalized.Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..];
        return normalized;
    }
}
