namespace CqrsLens;

/// <summary>
/// Cheap identity of the scan inputs. A cached scan is reused only when the file
/// count, newest write time and name/size/time hash all match.
/// </summary>
internal readonly record struct McpScanFingerprint(
    int FileCount,
    long MaxWriteTicks,
    ulong PathSizeAndTimeHash);
