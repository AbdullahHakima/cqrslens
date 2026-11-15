namespace CqrsLens;

/// <summary>
/// Stable identity of one symbol-derived architectural component: the <see cref="SymbolId"/>
/// of the declared symbol it is inventoried from. One declared symbol yields at most one
/// component (F0 §2, §2.1).
/// </summary>
public sealed record ComponentId : IComparable<ComponentId>
{
    private NodeReference? _nodeReference;

    public SymbolId Symbol { get; }
    public ProjectId Project => Symbol.Project;
    public string Value => ToString();

    internal NodeReference AsNodeReference() => _nodeReference ??= NodeReference.CreateComponent(this);

    public ComponentId(SymbolId symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        Symbol = symbol;
    }

    public static ComponentId FromSymbol(SymbolId symbol) => new(symbol);

    public bool Equals(ComponentId? other)
        => other is not null && Symbol.Equals(other.Symbol);

    public override int GetHashCode() => Symbol.GetHashCode();

    public int CompareTo(ComponentId? other)
        => other is null ? 1 : Symbol.CompareTo(other.Symbol);

    public static bool operator <(ComponentId? left, ComponentId? right)
        => left is null ? right is not null : left.CompareTo(right) < 0;

    public static bool operator <=(ComponentId? left, ComponentId? right)
        => left is null || left.CompareTo(right) <= 0;

    public static bool operator >(ComponentId? left, ComponentId? right)
        => left is not null && left.CompareTo(right) > 0;

    public static bool operator >=(ComponentId? left, ComponentId? right)
        => left is null ? right is null : left.CompareTo(right) >= 0;

    public override string ToString() => Symbol.ToString();
}
