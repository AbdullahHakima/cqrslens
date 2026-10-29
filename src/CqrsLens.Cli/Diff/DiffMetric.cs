namespace CqrsLens;

/// <summary>A base/head count pair. The delta is derived, never serialized separately.</summary>
internal sealed record DiffMetric(int Base, int Head)
{
    public int Delta => Head - Base;
}
