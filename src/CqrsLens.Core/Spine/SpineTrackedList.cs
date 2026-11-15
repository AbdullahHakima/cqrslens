namespace CqrsLens;

/// <summary>
/// Backing collection that synchronizes item lifecycle mutations (add, remove, clear, sort)
/// with <see cref="ArchitectureSpine"/> while preserving item order and detail fields.
/// </summary>
public sealed class SpineTrackedList<T> : SpineProjectedCollection<T>
{
    private readonly List<T> _items = new();
    private Action<T>? _onAdded;
    private Action<IReadOnlyList<T>>? _onRangeAdded;
    private Action<T>? _onRemoved;
    private Action? _onCleared;

    internal SpineTrackedList(
        Action<T>? onAdded = null,
        Action<T>? onRemoved = null,
        Action? onCleared = null,
        Action<IReadOnlyList<T>>? onRangeAdded = null)
    {
        _onAdded = onAdded;
        _onRangeAdded = onRangeAdded;
        _onRemoved = onRemoved;
        _onCleared = onCleared;
    }

    internal void BindHooks(
        Action<T> onAdded,
        Action<T> onRemoved,
        Action onCleared,
        Action<IReadOnlyList<T>>? onRangeAdded = null)
    {
        _onAdded = onAdded;
        _onRangeAdded = onRangeAdded;
        _onRemoved = onRemoved;
        _onCleared = onCleared;

        foreach (var item in _items)
            _onAdded(item);
    }

    protected override IReadOnlyList<T> Project() => _items;

    protected override void AddCore(T item)
    {
        _items.Add(item);
        _onAdded?.Invoke(item);
    }

    internal override void AddRangeCore(IEnumerable<T> items)
    {
        if (_onRangeAdded is null)
        {
            base.AddRangeCore(items);
            return;
        }

        List<T> added;
        if (items is ICollection<T> col)
        {
            if (col.Count == 0)
                return;
            added = new List<T>(col.Count);
            added.AddRange(col);
        }
        else
        {
            added = new List<T>();
            added.AddRange(items);
            if (added.Count == 0)
                return;
        }

        _items.AddRange(added);
        _onRangeAdded(added);
    }

    protected override void InsertCore(int index, T item)
    {
        _items.Insert(index, item);
        _onAdded?.Invoke(item);
    }

    protected override void SetAtCore(int index, T item)
    {
        var old = _items[index];
        _items[index] = item;
        _onRemoved?.Invoke(old);
        _onAdded?.Invoke(item);
    }

    protected override bool RemoveCore(T item)
    {
        if (!_items.Remove(item))
            return false;

        _onRemoved?.Invoke(item);
        return true;
    }

    protected override void RemoveAtCore(int index)
    {
        var old = _items[index];
        _items.RemoveAt(index);
        _onRemoved?.Invoke(old);
    }

    protected override void ClearCore()
    {
        _items.Clear();
        _onCleared?.Invoke();
    }

    protected override void SortCore(Comparison<T>? comparison)
    {
        if (comparison is null)
            _items.Sort();
        else
            _items.Sort(comparison);
    }
}
