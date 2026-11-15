namespace CqrsLens;

/// <summary>
/// One structured observation supporting a spine component role or relation (F0 §5.2).
/// Every occurrence carries its admitting <see cref="Rule"/>, <see cref="Origin"/>, ordered
/// <see cref="Via"/> path, <see cref="Strength"/>, and either a <see cref="Location"/> or an
/// explicit <see cref="LocationOmittedReason"/> (F0 §2).
/// </summary>
public sealed record EvidenceOccurrence : IComparable<EvidenceOccurrence>
{
    private readonly int _hashCode;

    public EvidenceRule Rule { get; }
    public EvidenceOrigin Origin { get; }
    public SourceLocation? Location { get; }
    public string? LocationOmittedReason { get; }
    public IReadOnlyList<NodeReference> Via { get; }
    public EvidenceStrength Strength { get; }
    public ResolutionSemantics? ResolutionSemantics { get; }
    public string? ScopeProjectName { get; }
    public bool IsTestProject { get; }
    internal bool ExcludeFromDispatchCallSites { get; set; }

    public EvidenceOccurrence(
        EvidenceRule rule,
        EvidenceOrigin origin,
        SourceLocation? location,
        EvidenceStrength strength,
        IReadOnlyList<NodeReference>? via = null,
        string? locationOmittedReason = null,
        ResolutionSemantics? resolutionSemantics = null,
        string? scopeProjectName = null,
        bool isTestProject = false)
    {
        if (location is null && string.IsNullOrWhiteSpace(locationOmittedReason))
        {
            throw new ArgumentException(
                "Evidence without a SourceLocation must state why via LocationOmittedReason.",
                nameof(locationOmittedReason));
        }

        Rule = rule;
        Origin = origin;
        Location = location;
        LocationOmittedReason = string.IsNullOrWhiteSpace(locationOmittedReason)
            ? null
            : locationOmittedReason.Trim();
        Via = CopyVia(via);
        Strength = strength;
        ResolutionSemantics = resolutionSemantics;
        ScopeProjectName = string.IsNullOrWhiteSpace(scopeProjectName) ? null : scopeProjectName.Trim();
        IsTestProject = isTestProject;

        _hashCode = ComputeHashCode(
            Rule,
            Origin,
            Location,
            LocationOmittedReason,
            Via,
            Strength,
            ResolutionSemantics,
            ScopeProjectName,
            IsTestProject);
    }

    public bool Equals(EvidenceOccurrence? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (_hashCode != other._hashCode) return false;

        return Rule == other.Rule
               && Origin == other.Origin
               && Strength == other.Strength
               && ResolutionSemantics == other.ResolutionSemantics
               && IsTestProject == other.IsTestProject
               && Equals(Location, other.Location)
               && string.Equals(LocationOmittedReason, other.LocationOmittedReason, StringComparison.Ordinal)
               && string.Equals(ScopeProjectName, other.ScopeProjectName, StringComparison.Ordinal)
               && ViaEquals(Via, other.Via);
    }

    public override int GetHashCode() => _hashCode;

    public int CompareTo(EvidenceOccurrence? other)
    {
        if (other is null) return 1;
        if (ReferenceEquals(this, other)) return 0;

        var cmp = ((int)Rule).CompareTo((int)other.Rule);
        if (cmp != 0) return cmp;

        cmp = ((int)Origin).CompareTo((int)other.Origin);
        if (cmp != 0) return cmp;

        cmp = ((int)Strength).CompareTo((int)other.Strength);
        if (cmp != 0) return cmp;

        cmp = CompareLocations(Location, other.Location);
        if (cmp != 0) return cmp;

        cmp = string.Compare(LocationOmittedReason, other.LocationOmittedReason, StringComparison.Ordinal);
        if (cmp != 0) return cmp;

        cmp = Nullable.Compare(ResolutionSemantics, other.ResolutionSemantics);
        if (cmp != 0) return cmp;

        cmp = string.Compare(ScopeProjectName, other.ScopeProjectName, StringComparison.Ordinal);
        if (cmp != 0) return cmp;

        cmp = IsTestProject.CompareTo(other.IsTestProject);
        if (cmp != 0) return cmp;

        return CompareVia(Via, other.Via);
    }

    public static bool operator <(EvidenceOccurrence? left, EvidenceOccurrence? right)
        => left is null ? right is not null : left.CompareTo(right) < 0;

    public static bool operator <=(EvidenceOccurrence? left, EvidenceOccurrence? right)
        => left is null || left.CompareTo(right) <= 0;

    public static bool operator >(EvidenceOccurrence? left, EvidenceOccurrence? right)
        => left is not null && left.CompareTo(right) > 0;

    public static bool operator >=(EvidenceOccurrence? left, EvidenceOccurrence? right)
        => left is null ? right is null : left.CompareTo(right) >= 0;

    private static NodeReference[] CopyVia(IReadOnlyList<NodeReference>? via)
    {
        if (via is null || via.Count == 0)
            return Array.Empty<NodeReference>();

        var result = new NodeReference[via.Count];
        for (var i = 0; i < via.Count; i++)
        {
            result[i] = via[i] ?? throw new ArgumentNullException(nameof(via), "Via path cannot contain null node references.");
        }
        return result;
    }

    private static bool ViaEquals(IReadOnlyList<NodeReference> left, IReadOnlyList<NodeReference> right)
    {
        if (left.Count != right.Count) return false;
        for (var i = 0; i < left.Count; i++)
        {
            if (!left[i].Equals(right[i]))
                return false;
        }
        return true;
    }

    private static int CompareVia(IReadOnlyList<NodeReference> left, IReadOnlyList<NodeReference> right)
    {
        var count = Math.Min(left.Count, right.Count);
        for (var i = 0; i < count; i++)
        {
            var cmp = left[i].CompareTo(right[i]);
            if (cmp != 0) return cmp;
        }
        return left.Count.CompareTo(right.Count);
    }

    private static int CompareLocations(SourceLocation? left, SourceLocation? right)
    {
        if (left is null && right is null) return 0;
        if (left is null) return -1;
        if (right is null) return 1;

        var cmp = string.Compare(left.File, right.File, StringComparison.Ordinal);
        if (cmp != 0) return cmp;
        cmp = left.Line.CompareTo(right.Line);
        return cmp != 0 ? cmp : left.Column.CompareTo(right.Column);
    }

    private static int ComputeHashCode(
        EvidenceRule rule,
        EvidenceOrigin origin,
        SourceLocation? location,
        string? locationOmittedReason,
        IReadOnlyList<NodeReference> via,
        EvidenceStrength strength,
        ResolutionSemantics? resolutionSemantics,
        string? scopeProjectName,
        bool isTestProject)
    {
        var hash = new HashCode();
        hash.Add(rule);
        hash.Add(origin);
        hash.Add(location);
        hash.Add(locationOmittedReason, StringComparer.Ordinal);
        hash.Add(via.Count);
        foreach (var node in via)
            hash.Add(node);
        hash.Add(strength);
        hash.Add(resolutionSemantics);
        hash.Add(scopeProjectName, StringComparer.Ordinal);
        hash.Add(isTestProject);
        return hash.ToHashCode();
    }
}
