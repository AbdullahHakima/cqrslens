namespace CqrsLens;

/// <summary>Chooses the report format from --format, the output extension, or slice streaming.</summary>
internal static class ReportFormatResolver
{
    public static ReportFormat Resolve(CliOptions options)
    {
        if (options.Format == "html" || HasOutputExtension(options, ".html"))
            return ReportFormat.Html;

        if (options.Format == "json" || HasOutputExtension(options, ".json"))
            return ReportFormat.Json;

        if (options.Format == "agent" ||
            options.SliceFilter is not null ||
            HasOutputExtension(options, ".agent.md"))
        {
            return ReportFormat.Agent;
        }

        return ReportFormat.Markdown;
    }

    public static string DefaultFileName(ReportFormat format) => format switch
    {
        ReportFormat.Html => "ARCHITECTURE.html",
        ReportFormat.Json => "ARCHITECTURE.json",
        ReportFormat.Agent => "ARCHITECTURE.agent.md",
        _ => "ARCHITECTURE.md",
    };

    private static bool HasOutputExtension(CliOptions options, string extension)
        => options.Format is null &&
           options.Output is not null &&
           options.Output.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
}
