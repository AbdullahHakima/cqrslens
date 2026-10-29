namespace CqrsLens;

/// <summary>
/// One bounded CQRS diagram and the requests it covers. Large solutions are
/// split by project and balanced into parts so no diagram approaches GitHub's
/// Mermaid limits. Every request appears in exactly one section.
/// </summary>
public sealed record DiagramSection(
    string? ProjectName,
    IReadOnlyList<CqrsRequest> Requests,
    string Source,
    int Part,
    int PartCount,
    int ProjectRequestCount,
    int HandlerCount,
    int ValidatorCount,
    int MissingHandlerCount,
    int MissingValidatorCount)
{
    /// <summary>Plain-text heading used by the Markdown and HTML reports.</summary>
    public string Heading => ProjectName is null
        ? PartCount > 1
            ? $"CQRS Map — {Requests.Count} of {ProjectRequestCount} requests (part {Part} of {PartCount})"
            : "CQRS Map"
        : PartCount > 1
            ? $"{ProjectName} — {Requests.Count} of {ProjectRequestCount} requests (part {Part} of {PartCount})"
            : $"{ProjectName} — {Requests.Count} requests";

    /// <summary>Plain-text scope caption used below the diagram.</summary>
    public string Caption
    {
        get
        {
            var parts = new List<string>
            {
                Count(Requests.Count, "request"),
                Count(HandlerCount, "handler"),
                Count(ValidatorCount, "validator"),
            };
            if (MissingHandlerCount > 0)
                parts.Add(Count(MissingHandlerCount, "MissingHandler finding"));
            if (MissingValidatorCount > 0)
                parts.Add(Count(MissingValidatorCount, "CommandWithoutValidator finding"));
            return string.Join(" · ", parts);
        }
    }

    private static string Count(int count, string noun)
        => count == 1 ? $"1 {noun}" : $"{count} {noun}s";
}
