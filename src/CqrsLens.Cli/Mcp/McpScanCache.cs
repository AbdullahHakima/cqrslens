namespace CqrsLens;

/// <summary>Thread-safe, bounded LRU cache of scan results keyed by root and analysis mode.</summary>
internal sealed class McpScanCache
{
    internal const int MaxEntries = 8;

    private sealed record CacheEntry(string Key, McpCachedScan Scan);

    private readonly object _gate = new();
    private readonly Dictionary<string, LinkedListNode<CacheEntry>> _scans = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<CacheEntry> _lru = new();

    public bool TryGet(string key, McpScanFingerprint fingerprint, out AnalysisResult result)
    {
        lock (_gate)
        {
            if (_scans.TryGetValue(key, out var node))
            {
                if (node.Value.Scan.Fingerprint == fingerprint)
                {
                    _lru.Remove(node);
                    _lru.AddFirst(node);
                    result = node.Value.Scan.Result;
                    return true;
                }

                _lru.Remove(node);
                _scans.Remove(key);
            }

            result = null!;
            return false;
        }
    }

    public void Set(string key, AnalysisResult result, McpScanFingerprint fingerprint)
    {
        lock (_gate)
        {
            var entry = new CacheEntry(key, new McpCachedScan(result, fingerprint));
            if (_scans.TryGetValue(key, out var existing))
            {
                existing.Value = entry;
                _lru.Remove(existing);
                _lru.AddFirst(existing);
                return;
            }

            if (_scans.Count >= MaxEntries && _lru.Last is { } oldest)
            {
                _lru.RemoveLast();
                _scans.Remove(oldest.Value.Key);
            }

            var node = new LinkedListNode<CacheEntry>(entry);
            _lru.AddFirst(node);
            _scans[key] = node;
        }
    }
}
