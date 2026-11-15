namespace CqrsLens;

/// <summary>
/// Explicit container for unproven, ambiguous, and unresolved populations recorded
/// separately from proven relations in the spine (F0 §5.4, §4.3).
/// </summary>
public sealed class UnprovenPopulations
{
    private readonly List<UnresolvedDispatch> _unresolvedDispatches = new();
    private readonly List<AmbiguousResolutionEntry> _ambiguousResolutions = new();
    private readonly List<UnprovenDiagnosticEntry> _ambiguousLinks = new();
    private readonly List<UnprovenDiagnosticEntry> _unresolvedContracts = new();
    private readonly List<UnestablishedBehaviorEntry> _unestablishedBehaviors = new();

    public IReadOnlyList<UnresolvedDispatch> UnresolvedDispatches => _unresolvedDispatches;
    public IReadOnlyList<AmbiguousResolutionEntry> AmbiguousResolutions => _ambiguousResolutions;
    public IReadOnlyList<UnprovenDiagnosticEntry> AmbiguousLinks => _ambiguousLinks;
    public IReadOnlyList<UnprovenDiagnosticEntry> UnresolvedContracts => _unresolvedContracts;
    public IReadOnlyList<UnestablishedBehaviorEntry> UnestablishedBehaviors => _unestablishedBehaviors;

    public int TotalCount =>
        _unresolvedDispatches.Count
        + _ambiguousResolutions.Count
        + _ambiguousLinks.Count
        + _unresolvedContracts.Count
        + _unestablishedBehaviors.Count;

    public void AddUnresolvedDispatch(UnresolvedDispatch dispatch)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        _unresolvedDispatches.Add(dispatch);
    }

    public void AddAmbiguousResolution(AmbiguousResolutionEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!_ambiguousResolutions.Any(e => string.Equals(e.InterfaceSignature, entry.InterfaceSignature, StringComparison.Ordinal)))
            _ambiguousResolutions.Add(entry);
    }

    public void AddAmbiguousLink(UnprovenDiagnosticEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!_ambiguousLinks.Contains(entry))
            _ambiguousLinks.Add(entry);
    }

    public void AddUnresolvedContract(UnprovenDiagnosticEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!_unresolvedContracts.Contains(entry))
            _unresolvedContracts.Add(entry);
    }

    public void AddUnestablishedBehavior(UnestablishedBehaviorEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!_unestablishedBehaviors.Contains(entry))
            _unestablishedBehaviors.Add(entry);
    }

    public void Clear()
    {
        _unresolvedDispatches.Clear();
        _ambiguousResolutions.Clear();
        _ambiguousLinks.Clear();
        _unresolvedContracts.Clear();
        _unestablishedBehaviors.Clear();
    }

    internal void InsertUnresolvedDispatch(int index, UnresolvedDispatch dispatch)
        => _unresolvedDispatches.Insert(index, dispatch);

    internal void SetUnresolvedDispatch(int index, UnresolvedDispatch dispatch)
        => _unresolvedDispatches[index] = dispatch;

    internal bool RemoveUnresolvedDispatch(UnresolvedDispatch dispatch)
        => _unresolvedDispatches.Remove(dispatch);

    internal void RemoveUnresolvedDispatchAt(int index)
        => _unresolvedDispatches.RemoveAt(index);

    internal void ClearUnresolvedDispatches()
        => _unresolvedDispatches.Clear();

    internal void SortUnresolvedDispatches(Comparison<UnresolvedDispatch> comparison)
        => _unresolvedDispatches.Sort(comparison);

    internal int RemoveAmbiguousResolution(string interfaceSignature)
        => _ambiguousResolutions.RemoveAll(e =>
            string.Equals(e.InterfaceSignature, interfaceSignature, StringComparison.Ordinal));

    internal void ClearAmbiguousResolutions()
        => _ambiguousResolutions.Clear();
}
