namespace CqrsLens;

/// <summary>One measured analysis stage.</summary>
public sealed record AnalysisPhaseTiming(string Name, double ElapsedMilliseconds, long AllocatedBytes);
