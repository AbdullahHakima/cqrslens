namespace CqrsLens;

/// <summary>
/// Canonical deduplication key for a logical relation:
/// <c>(Kind, From, To, ordered Via path, Method-if-any)</c> (F0 §5.1 v2.1).
/// Different <see cref="Via"/> element orders yield distinct keys; identical keys
/// merge into multiple evidence occurrences of one logical <see cref="SpineRelation"/>.
/// </summary>
public sealed record RelationKey : IComparable<RelationKey>
{
    private readonly int _hashCode;

    public RelationKind Kind { get; }
    public NodeReference From { get; }
    public NodeReference To { get; }
    public IReadOnlyList<NodeReference> Via { get; }
    public string? Method { get; }

    public RelationKey(
        RelationKind kind,
        NodeReference from,
        NodeReference to,
        IReadOnlyList<NodeReference>? via = null,
        string? method = null)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        Kind = kind;
        From = from;
        To = to;
        Via = CopyVia(via);
        Method = string.IsNullOrWhiteSpace(method) ? null : method.Trim();

        _hashCode = ComputeHashCode(Kind, From, To, Via, Method);
    }

    public bool Equals(RelationKey? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (_hashCode != other._hashCode) return false;

        return Kind == other.Kind
               && From.Equals(other.From)
               && To.Equals(other.To)
               && string.Equals(Method, other.Method, StringComparison.Ordinal)
               && ViaEquals(Via, other.Via);
    }

    public override int GetHashCode() => _hashCode;

    public int CompareTo(RelationKey? other)
    {
        if (other is null) return 1;
        if (ReferenceEquals(this, other)) return 0;

        var cmp = ((int)Kind).CompareTo((int)other.Kind);
        if (cmp != 0) return cmp;

        cmp = From.CompareTo(other.From);
        if (cmp != 0) return cmp;

        cmp = To.CompareTo(other.To);
        if (cmp != 0) return cmp;

        cmp = string.Compare(Method, other.Method, StringComparison.Ordinal);
        if (cmp != 0) return cmp;

        return CompareVia(Via, other.Via);
    }

    public static bool operator <(RelationKey? left, RelationKey? right)
        => left is null ? right is not null : left.CompareTo(right) < 0;

    public static bool operator <=(RelationKey? left, RelationKey? right)
        => left is null || left.CompareTo(right) <= 0;

    public static bool operator >(RelationKey? left, RelationKey? right)
        => left is not null && left.CompareTo(right) > 0;

    public static bool operator >=(RelationKey? left, RelationKey? right)
        => left is null ? right is null : left.CompareTo(right) >= 0;

    public override string ToString()
    {
        var viaText = Via.Count == 0
            ? ""
            : $" via [{string.Join(" -> ", Via.Select(v => v.Name))}]";
        var methodText = Method is null ? "" : $".{Method}()";
        return $"{Kind}: {From} -> {To}{methodText}{viaText}";
    }

    internal static bool ViaEquals(IReadOnlyList<NodeReference> left, IReadOnlyList<NodeReference> right)
    {
        if (left.Count != right.Count) return false;
        for (var i = 0; i < left.Count; i++)
        {
            if (!left[i].Equals(right[i]))
                return false;
        }
        return true;
    }

    private static NodeReference[] CopyVia(IReadOnlyList<NodeReference>? via)
    {
        if (via is null || via.Count == 0)
            return Array.Empty<NodeReference>();

        var result = new NodeReference[via.Count];
        for (var i = 0; i < via.Count; i++)
            result[i] = via[i] ?? throw new ArgumentNullException(nameof(via));
        return result;
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

    private static int ComputeHashCode(
        RelationKind kind,
        NodeReference from,
        NodeReference to,
        IReadOnlyList<NodeReference> via,
        string? method)
    {
        var hash = new HashCode();
        hash.Add(kind);
        hash.Add(from);
        hash.Add(to);
        hash.Add(via.Count);
        foreach (var item in via)
            hash.Add(item);
        hash.Add(method, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
}
