namespace CqrsLens;

/// <summary>A cached scan and the input fingerprint it was produced from.</summary>
internal sealed record McpCachedScan(AnalysisResult Result, McpScanFingerprint Fingerprint);
