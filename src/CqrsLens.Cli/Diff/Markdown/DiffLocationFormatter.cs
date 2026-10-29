using static CqrsLens.JsonReportGenerator;

namespace CqrsLens;

/// <summary>Formats a finding location for the diff report, rebased to the CI root when known.</summary>
internal static class DiffLocationFormatter
{
    public static string Format(JsonLocation? location, AnalysisResult? headResult, string? ciRoot)
    {
        if (location is null || string.IsNullOrEmpty(location.File))
            return "—";

        var file = headResult is not null
            ? CiPath.Relative(headResult, location.File, ciRoot)
            : location.File.Replace('\\', '/');
        return ReportGenerator.TableCell($"{file}:{location.Line}:{location.Column}");
    }
}
