namespace CqrsLens;

/// <summary>Per-server cache of scan results keyed by root and analysis mode.</summary>
internal sealed class McpScanCache
{
    private readonly Dictionary<string, McpCachedScan> _scans = new(StringComparer.OrdinalIgnoreCase);

    public bool TryGet(string key, McpScanFingerprint fingerprint, out AnalysisResult result)
    {
        if (_scans.TryGetValue(key, out var cached) && cached.Fingerprint == fingerprint)
        {
            result = cached.Result;
            return true;
        }

        result = null!;
        return false;
    }

    public void Set(string key, AnalysisResult result, McpScanFingerprint fingerprint)
        => _scans[key] = new McpCachedScan(result, fingerprint);
}
