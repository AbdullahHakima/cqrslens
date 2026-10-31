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

    public static string GenerateDnaAndIndex(AnalysisResult result, bool includeIndex = false)
        => AgentDnaIndexGenerator.Generate(result, includeIndex);

    public static string GenerateEntityImpact(
        AnalysisResult result,
        string entityFilter,
        bool containsFilter = false,
        int maxResults = 0)
        => AgentEntityImpactGenerator.Generate(result, entityFilter, containsFilter, maxResults);

    public static string GenerateChecksSummary(AnalysisResult result)
        => AgentChecksSummaryGenerator.Generate(result);
}
