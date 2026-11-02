using System.Globalization;

namespace CqrsLens;

/// <summary>
/// Builds the validation-coverage figure: per project (and solution-wide) the
/// request total, how many requests have a linked validator, and the coverage
/// percentage. "Linked" is the model's own request → validator linkage — the same
/// <c>CqrsRequest.Validators</c> the CommandWithoutValidator check reads — never a
/// second name-matching mechanism. Presentation only; no score or grade.
/// </summary>
internal static class ValidationCoverageView
{
    internal const int UncoveredPercentDecimals = 1;

    /// <summary>One project (or the solution) row. <see cref="Percent"/> is null when there are no requests.</summary>
    internal sealed record Row(string Project, int Requests, int Covered, int Validators)
    {
        internal double? Percent => Requests == 0
            ? null
            : Math.Round(Covered * 100.0 / Requests, UncoveredPercentDecimals);
    }

    internal sealed record Summary(IReadOnlyList<Row> Projects, Row Solution);

    internal static Summary Build(AnalysisResult result)
    {
        var requestProjects = result.Requests
            .GroupBy(r => r.ProjectName ?? MermaidDiagramText.UnassignedProject, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (Requests: g.Count(), Covered: g.Count(r => r.Validators.Count > 0)),
                StringComparer.Ordinal);

        var validatorsByProject = result.Validators
            .GroupBy(v => v.ProjectName ?? MermaidDiagramText.UnassignedProject, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var projects = requestProjects.Keys
            .Concat(validatorsByProject.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(name => name, StringComparer.Ordinal)
            .Select(name =>
            {
                requestProjects.TryGetValue(name, out var coverage);
                return new Row(
                    name,
                    coverage.Requests,
                    coverage.Covered,
                    validatorsByProject.TryGetValue(name, out var validators) ? validators : 0);
            })
            .ToList();

        var solution = new Row(
            "All projects",
            result.Requests.Count,
            result.Requests.Count(r => r.Validators.Count > 0),
            result.Validators.Count);

        return new Summary(projects, solution);
    }

    internal static string CoverageText(Row row)
        => row.Percent is null
            ? "no requests"
            : row.Percent.Value.ToString("0.0", CultureInfo.InvariantCulture) + "%";
}
