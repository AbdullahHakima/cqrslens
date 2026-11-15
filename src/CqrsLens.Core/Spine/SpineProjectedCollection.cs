using System.Collections;

namespace CqrsLens;

/// <summary>
/// Live projection collection backed by <see cref="ArchitectureSpine"/>. Reads project
/// current spine state without duplicating edge or link storage; mutations update the
/// underlying spine directly.
/// </summary>
public abstract class SpineProjectedCollection<T> : IList<T>, IReadOnlyList<T>
{
    private long _cachedStamp = -1;
    private IReadOnlyList<T>? _cachedProjection;

    public int Count => ReadProjection().Count;
    public bool IsReadOnly => false;

    public T this[int index]
    {
        get => ReadProjection()[index];
        set => SetAtCore(index, value);
    }

    protected virtual long CacheStamp => -1;

    protected void InvalidateCache() => _cachedStamp = -1;

    private IReadOnlyList<T> ReadProjection()
    {
        var stamp = CacheStamp;
        if (stamp < 0)
            return Project();

        if (_cachedProjection is null || _cachedStamp != stamp)
        {
            _cachedProjection = Project();
            _cachedStamp = stamp;
        }

        return _cachedProjection;
    }

    protected abstract IReadOnlyList<T> Project();
    protected abstract void AddCore(T item);
    protected virtual void InsertCore(int index, T item) => AddCore(item);
    protected virtual void SetAtCore(int index, T item) => throw new NotSupportedException();
    protected virtual bool RemoveCore(T item) => false;
    protected virtual void RemoveAtCore(int index)
    {
        var snapshot = ReadProjection();
        if ((uint)index >= (uint)snapshot.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        RemoveCore(snapshot[index]);
    }
    protected virtual void ClearCore() { }
    protected virtual void SortCore(Comparison<T>? comparison) { }

    internal virtual void AddRangeCore(IEnumerable<T> items)
    {
        foreach (var item in items)
            AddCore(item);
    }

    public void Add(T item) => AddCore(item);

    public void AddRange(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        AddRangeCore(items);
    }

    public void Insert(int index, T item) => InsertCore(index, item);

    public bool Remove(T item) => RemoveCore(item);

    public void RemoveAt(int index) => RemoveAtCore(index);

    public int RemoveAll(Predicate<T> match)
    {
        ArgumentNullException.ThrowIfNull(match);
        var snapshot = ReadProjection();
        var toRemove = new List<T>();
        for (var i = 0; i < snapshot.Count; i++)
        {
            if (match(snapshot[i]))
                toRemove.Add(snapshot[i]);
        }

        var removed = 0;
        foreach (var item in toRemove)
        {
            if (RemoveCore(item))
                removed++;
        }
        return removed;
    }

    public void Clear() => ClearCore();

    public bool Contains(T item)
    {
        var snapshot = ReadProjection();
        var comparer = EqualityComparer<T>.Default;
        for (var i = 0; i < snapshot.Count; i++)
        {
            if (comparer.Equals(snapshot[i], item))
                return true;
        }
        return false;
    }

    public int IndexOf(T item)
    {
        var snapshot = ReadProjection();
        var comparer = EqualityComparer<T>.Default;
        for (var i = 0; i < snapshot.Count; i++)
        {
            if (comparer.Equals(snapshot[i], item))
                return i;
        }
        return -1;
    }

    public T? Find(Predicate<T> match)
    {
        ArgumentNullException.ThrowIfNull(match);
        var snapshot = ReadProjection();
        for (var i = 0; i < snapshot.Count; i++)
        {
            if (match(snapshot[i]))
                return snapshot[i];
        }
        return default;
    }

    public bool Exists(Predicate<T> match)
    {
        ArgumentNullException.ThrowIfNull(match);
        var snapshot = ReadProjection();
        for (var i = 0; i < snapshot.Count; i++)
        {
            if (match(snapshot[i]))
                return true;
        }
        return false;
    }

    public void Sort() => SortCore(null);

    public void Sort(IComparer<T>? comparer)
        => SortCore(comparer is null ? null : comparer.Compare);

    public void Sort(Comparison<T> comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        SortCore(comparison);
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        ArgumentNullException.ThrowIfNull(array);
        var snapshot = ReadProjection();
        for (var i = 0; i < snapshot.Count; i++)
            array[arrayIndex + i] = snapshot[i];
    }

    public IEnumerator<T> GetEnumerator() => ReadProjection().GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
