namespace CqrsLens;

/// <summary>Prints per-phase timing and allocation totals for <c>--profile</c>.</summary>
internal static class ProfileWriter
{
    public static void Write(AnalysisResult result, TextWriter writer)
    {
        static string Bytes(long value) => value >= 1_048_576
            ? $"{value / 1_048_576.0:0.0} MB"
            : $"{value / 1024.0:0.0} KB";

        writer.WriteLine(
            $"Profile ({result.ScannerName}): {result.ElapsedMilliseconds} ms, " +
            $"{Bytes(result.AllocatedBytes)} allocated");
        foreach (var phase in result.PhaseTimings)
            writer.WriteLine($"  {phase.Name,-30} {phase.ElapsedMilliseconds,8:0.00} ms {Bytes(phase.AllocatedBytes),10}");
    }
}
