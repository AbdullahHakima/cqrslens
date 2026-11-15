namespace CqrsLens;

/// <summary>
/// Mapping-level identity of an HTTP endpoint: declaring <see cref="Project"/>, mapping-site
/// source location, and a stable source-order <see cref="Ordinal"/> when one site declares
/// several mappings. Route and verb are properties of the endpoint, never its identity (F0 §2 v2.1).
/// </summary>
public sealed record EndpointId : IComparable<EndpointId>
{
    private readonly int _hashCode;
    private string? _toString;
    private NodeReference? _nodeReference;

    public ProjectId Project { get; }
    public SourceLocation? MappingSite { get; }
    public string SiteKey { get; }
    public int Ordinal { get; }

    private EndpointId(
        ProjectId project,
        SourceLocation? mappingSite,
        string siteKey,
        int ordinal)
    {
        Project = project;
        MappingSite = mappingSite;
        SiteKey = siteKey;
        Ordinal = ordinal;

        var hash = new HashCode();
        hash.Add(project);
        hash.Add(siteKey, StringComparer.Ordinal);
        hash.Add(ordinal);
        _hashCode = hash.ToHashCode();
    }

    public static EndpointId Create(ProjectId project, SourceLocation mappingSite, int ordinal = 0)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(mappingSite);
        if (ordinal < 0)
            throw new ArgumentOutOfRangeException(nameof(ordinal), "Endpoint ordinal cannot be negative.");

        var normalizedFile = mappingSite.File.Replace('\\', '/');
        var normalizedSite = normalizedFile == mappingSite.File
            ? mappingSite
            : mappingSite with { File = normalizedFile };

        return new EndpointId(project, normalizedSite, normalizedSite.ToString(), ordinal);
    }

    internal static EndpointId FromSyntheticSite(ProjectId project, string siteKey, int ordinal = 0)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(siteKey);
        if (ordinal < 0)
            throw new ArgumentOutOfRangeException(nameof(ordinal), "Endpoint ordinal cannot be negative.");

        return new EndpointId(project, null, siteKey.Trim(), ordinal);
    }

    public bool Equals(EndpointId? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (_hashCode != other._hashCode) return false;

        return Ordinal == other.Ordinal
               && Project.Equals(other.Project)
               && string.Equals(SiteKey, other.SiteKey, StringComparison.Ordinal);
    }

    public override int GetHashCode() => _hashCode;

    public int CompareTo(EndpointId? other)
    {
        if (other is null) return 1;
        if (ReferenceEquals(this, other)) return 0;

        var cmp = Project.CompareTo(other.Project);
        if (cmp != 0) return cmp;

        if (MappingSite is not null && other.MappingSite is not null)
        {
            cmp = string.Compare(MappingSite.File, other.MappingSite.File, StringComparison.Ordinal);
            if (cmp != 0) return cmp;
            cmp = MappingSite.Line.CompareTo(other.MappingSite.Line);
            if (cmp != 0) return cmp;
            cmp = MappingSite.Column.CompareTo(other.MappingSite.Column);
            if (cmp != 0) return cmp;
        }
        else
        {
            cmp = string.Compare(SiteKey, other.SiteKey, StringComparison.Ordinal);
            if (cmp != 0) return cmp;
        }

        return Ordinal.CompareTo(other.Ordinal);
    }

    public static bool operator <(EndpointId? left, EndpointId? right)
        => left is null ? right is not null : left.CompareTo(right) < 0;

    public static bool operator <=(EndpointId? left, EndpointId? right)
        => left is null || left.CompareTo(right) <= 0;

    public static bool operator >(EndpointId? left, EndpointId? right)
        => left is not null && left.CompareTo(right) > 0;

    public static bool operator >=(EndpointId? left, EndpointId? right)
        => left is null ? right is null : left.CompareTo(right) >= 0;

    internal NodeReference AsNodeReference() => _nodeReference ??= NodeReference.CreateEndpoint(this);

    public override string ToString() => _toString ??= $"{Project.Value}::{SiteKey}#{Ordinal}";
}
