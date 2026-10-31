using System.Diagnostics;

namespace CqrsLens;

/// <summary>
/// Records wall time and allocated bytes for one analysis phase. Used by the
/// pipeline and by scanners that expose their own internal stages.
/// </summary>
internal sealed class AnalysisPhaseRecorder
{
    private readonly AnalysisResult _result;
    private readonly Stopwatch _watch = Stopwatch.StartNew();
    private long _allocatedStart = GC.GetTotalAllocatedBytes(precise: false);

    public AnalysisPhaseRecorder(AnalysisResult result) => _result = result;

    public void Record(string name)
    {
        _watch.Stop();
        var allocated = GC.GetTotalAllocatedBytes(precise: false) - _allocatedStart;
        _result.PhaseTimings.Add(new AnalysisPhaseTiming(
            name, _watch.Elapsed.TotalMilliseconds, allocated));
        _watch.Restart();
        _allocatedStart = GC.GetTotalAllocatedBytes(precise: false);
    }
}
