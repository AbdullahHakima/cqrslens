namespace CqrsLens;

/// <summary>
/// Generates a token-efficient, Mermaid-free architectural context map (<c>--format agent</c>
/// and <c>--slice &lt;filter&gt;</c>) designed specifically for AI coding agents and LLMs.
/// Synthesizes project conventions ("Architectural DNA") and traces full vertical slices
/// from API Endpoint → Request(Payload) → Validator → Handler(Dependencies) → DbSet &amp; Events
/// with exact <c>File:Line</c> source pointers.
/// </summary>
public static class AgentReportGenerator
{
    public static string Generate(AnalysisResult result, string? sliceFilter = null, int maxSlices = 0)
        => AgentArchitectureMapGenerator.Generate(result, sliceFilter, maxSlices);

    /// <summary>
    /// Returns the matched slice(s) as compact typed JSON instead of the rendered text form. Every
    /// value comes from the scan model alone; anything the model does not hold is absent rather than
    /// guessed. Behaviors carry the model's registered-order / not-guaranteed-execution-order label.
    /// </summary>
    public static string GenerateSliceData(AnalysisResult result, string? sliceFilter = null, int maxSlices = 0)
        => AgentSliceDataGenerator.Generate(result, sliceFilter, maxSlices);

    public static string GenerateDnaAndIndex(AnalysisResult result, bool includeIndex = false)
        => AgentDnaIndexGenerator.Generate(result, includeIndex);

    public static string GenerateEntityImpact(
        AnalysisResult result,
        string entityFilter,
        bool containsFilter = false,
        int maxResults = 0)
        => AgentEntityImpactGenerator.Generate(result, entityFilter, containsFilter, maxResults);

    /// <summary>
    /// Traces one request's impact: what it touches (handler, validators, pipeline behaviors in
    /// registered order, dispatching endpoints, published notifications and their handlers, and
    /// the model's handler→DbContext edges) and what references it (reverse reach). Every fact
    /// comes from the scan model alone; absence is stated as absence, and an unknown name is a
    /// clean not-found answer.
    /// </summary>
    public static string GenerateRequestImpact(
        AnalysisResult result,
        string requestFilter,
        bool containsFilter = false,
        int maxResults = 0)
        => AgentRequestImpactGenerator.Generate(result, requestFilter, containsFilter, maxResults);

    public static string GenerateChecksSummary(AnalysisResult result)
        => AgentChecksSummaryGenerator.Generate(result);

    /// <summary>
    /// Returns the active architecture findings filtered by rule code, severity and target scope,
    /// paged by <see cref="CheckFilter.Limit"/>. With an empty filter the legacy answer is returned
    /// byte-for-byte.
    /// </summary>
    internal static string GenerateChecksSummary(AnalysisResult result, CheckFilter filter)
        => AgentChecksSummaryGenerator.Generate(result, filter);

    /// <summary>
    /// Returns the active architecture findings grouped by project (module), counts first, after
    /// the same filters the filtered view applies. With no grouping requested the caller uses the
    /// unfiltered/filtered answer, which is left byte-for-byte unchanged.
    /// </summary>
    internal static string GenerateChecksGrouped(AnalysisResult result, CheckFilter filter, string groupBy)
        => AgentChecksSummaryGenerator.GenerateGrouped(result, filter, groupBy);

    /// <summary>
    /// Verifies a structured design claim against the scan model and returns exactly one verdict
    /// (supported / not supported / not determinable) with its evidence or reason.
    /// </summary>
    public static string VerifyClaim(AnalysisResult result, string? subject, string? relation, string? claimObject)
        => AgentClaimVerifier.Format(AgentClaimVerifier.Verify(result, subject, relation, claimObject));
}
