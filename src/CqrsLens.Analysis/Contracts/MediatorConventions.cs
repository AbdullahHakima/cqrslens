namespace CqrsLens;

/// <summary>
/// Built-in conventions for mediator libraries other than MediatR:
/// <list type="bullet">
/// <item><description><b>Mediator</b> (martinothamar/Mediator): <c>ICommand</c>, <c>IQuery</c>,
/// <c>IStream*</c> request markers and their <c>I*Handler</c> roots, enabled automatically
/// when the scanned source references the <c>Mediator</c> package or namespace.</description></item>
/// <item><description><b>Wolverine</b> (WolverineFx): convention-based handlers â€” classes named
/// <c>*Handler</c> / <c>*Consumer</c> with <c>Handle</c>/<c>HandleAsync</c>/<c>Consume</c>/<c>ConsumeAsync</c>
/// methods whose first parameter is the message type.</description></item>
/// </list>
/// </summary>
internal static class MediatorConventions
{
    public const string MediatorName = "Mediator";
    public const string WolverineName = "Wolverine";

    public static readonly IReadOnlyList<string> MediatorRequestMarkers = new[]
    {
        "ICommand", "IQuery", "IBaseCommand", "IBaseQuery",
        "IStreamRequest", "IStreamCommand", "IStreamQuery",
    };

    public static readonly IReadOnlyList<string> MediatorHandlerMarkers = new[]
    {
        "ICommandHandler", "IQueryHandler",
        "IStreamRequestHandler", "IStreamCommandHandler", "IStreamQueryHandler",
    };

    /// <summary>Detects which built-in conventions apply to the scanned source.</summary>
    public static IReadOnlyList<string> Detect(
        IReadOnlyList<ProjectInfo> projects,
        IReadOnlyList<SourceFile> files)
        => MediatorLibraryDetector.Detect(projects, files);

    /// <summary>Request markers implied by the detected conventions plus the configured ones.</summary>
    public static IReadOnlySet<string> EffectiveMarkers(IReadOnlySet<string> configured, IReadOnlyList<string> conventions)
    {
        if (!conventions.Contains(MediatorName)) return configured;
        var set = new HashSet<string>(configured, StringComparer.Ordinal);
        set.UnionWith(MediatorRequestMarkers);
        return set;
    }

    /// <summary>Handler roots implied by the detected conventions plus the configured ones.</summary>
    public static IReadOnlySet<string> EffectiveHandlerMarkers(IReadOnlySet<string> configured, IReadOnlyList<string> conventions)
    {
        if (!conventions.Contains(MediatorName)) return configured;
        var set = new HashSet<string>(configured, StringComparer.Ordinal);
        set.UnionWith(MediatorHandlerMarkers);
        return set;
    }

    /// <summary>Adds Wolverine convention handlers and the messages they handle.</summary>
    public static void ApplyWolverine(IReadOnlyList<SourceFile> files, AnalysisResult result)
        => WolverineHandlerDiscovery.Apply(files, result);
}
