namespace CqrsLens;

/// <summary>
/// Live projection of <see cref="AnalysisResult.UnresolvedDispatches"/> backed directly by
/// <see cref="UnprovenPopulations.UnresolvedDispatches"/> in <see cref="ArchitectureSpine"/>.
/// </summary>
public sealed class SpineUnresolvedDispatchList : SpineProjectedCollection<UnresolvedDispatch>
{
    private readonly AnalysisResult _result;

    internal SpineUnresolvedDispatchList(AnalysisResult result)
    {
        _result = result;
    }

    protected override IReadOnlyList<UnresolvedDispatch> Project()
        => _result.Spine.Unproven.UnresolvedDispatches;

    protected override void AddCore(UnresolvedDispatch item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _result.Spine.Unproven.AddUnresolvedDispatch(item);
    }

    protected override void InsertCore(int index, UnresolvedDispatch item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _result.Spine.Unproven.InsertUnresolvedDispatch(index, item);
    }

    protected override void SetAtCore(int index, UnresolvedDispatch item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _result.Spine.Unproven.SetUnresolvedDispatch(index, item);
    }

    protected override bool RemoveCore(UnresolvedDispatch item)
        => item is not null && _result.Spine.Unproven.RemoveUnresolvedDispatch(item);

    protected override void RemoveAtCore(int index)
        => _result.Spine.Unproven.RemoveUnresolvedDispatchAt(index);

    protected override void ClearCore()
        => _result.Spine.Unproven.ClearUnresolvedDispatches();

    protected override void SortCore(Comparison<UnresolvedDispatch>? comparison)
    {
        if (comparison is not null)
            _result.Spine.Unproven.SortUnresolvedDispatches(comparison);
    }
}
