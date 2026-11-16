namespace CqrsLens;

/// <summary>
/// One logical directed relation in the architecture spine, identified uniquely by its
/// <see cref="Key"/> and backed by one or more <see cref="EvidenceOccurrence"/> observations
/// (F0 §4, §5.1).
/// </summary>
public sealed class SpineRelation
{
    private readonly List<EvidenceOccurrence> _occurrences = new();
    private EvidenceOccurrence[]? _sortedOccurrences;

    public RelationKey Key { get; }
    public RelationKind Kind => Key.Kind;
    public NodeReference From => Key.From;
    public NodeReference To => Key.To;
    public IReadOnlyList<NodeReference> Via => Key.Via;
    public string? Method => Key.Method;

    internal long DiscoverySequence { get; set; }
    internal EvidenceOccurrence FirstOccurrence => _occurrences[0];

    public SpineRelation(
        RelationKind kind,
        NodeReference from,
        NodeReference to,
        EvidenceOccurrence initialOccurrence,
        string? method = null)
        : this(
            new RelationKey(
                kind,
                from ?? throw new ArgumentNullException(nameof(from)),
                to ?? throw new ArgumentNullException(nameof(to)),
                (initialOccurrence ?? throw new ArgumentNullException(nameof(initialOccurrence))).Via,
                method),
            initialOccurrence)
    {
    }

    internal SpineRelation(RelationKey key, EvidenceOccurrence initialOccurrence)
    {
        Key = key;
        _occurrences.Add(initialOccurrence);
    }

    public IReadOnlyList<EvidenceOccurrence> Occurrences
    {
        get
        {
            if (_occurrences.Count <= 1)
                return _occurrences;
            if (_sortedOccurrences is not null)
                return _sortedOccurrences;

            var arr = _occurrences.ToArray();
            Array.Sort(arr);
            _sortedOccurrences = arr;
            return arr;
        }
    }

    public int OccurrenceCount => _occurrences.Count;

    public EvidenceStrength Strength
    {
        get
        {
            if (_occurrences.Count == 1)
                return _occurrences[0].Strength;

            var best = EvidenceStrength.Inferred;
            for (var i = 0; i < _occurrences.Count; i++)
            {
                var strength = _occurrences[i].Strength;
                if (strength < best)
                    best = strength;
            }
            return best;
        }
    }

    public void AddOccurrence(EvidenceOccurrence occurrence)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        if (!RelationKey.ViaEquals(Key.Via, occurrence.Via))
        {
            throw new ArgumentException(
                "Occurrence Via path must match the relation deduplication key.",
                nameof(occurrence));
        }

        var existingIndex = _occurrences.IndexOf(occurrence);
        if (existingIndex < 0)
        {
            _occurrences.Add(occurrence);
            _sortedOccurrences = null;
        }
        else if (!occurrence.ExcludeFromDispatchCallSites && _occurrences[existingIndex].ExcludeFromDispatchCallSites)
        {
            _occurrences[existingIndex].ExcludeFromDispatchCallSites = false;
        }
    }

    internal bool TryReplaceOccurrence(EvidenceOccurrence existing, EvidenceOccurrence replacement)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(replacement);
        if (!RelationKey.ViaEquals(Key.Via, replacement.Via))
            return false;

        var existingIndex = _occurrences.IndexOf(existing);
        if (existingIndex < 0)
            return false;

        var duplicateIndex = _occurrences.IndexOf(replacement);
        if (duplicateIndex >= 0 && duplicateIndex != existingIndex)
        {
            _occurrences.RemoveAt(existingIndex);
        }
        else
        {
            _occurrences[existingIndex] = replacement;
        }

        _sortedOccurrences = null;
        return true;
    }
}
