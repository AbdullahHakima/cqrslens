using System.Text;
using static CqrsLens.AgentReportFormatter;
using static CqrsLens.AgentReportLookups;

namespace CqrsLens;

/// <summary>Writes the Architectural DNA and convention summary.</summary>
internal static class AgentArchitectureDnaWriter
{
    internal static void AppendArchitecturalDna(
        StringBuilder sb,
        AnalysisResult result,
        IReadOnlyDictionary<string, HandlerInfo> handlersByFull,
        IReadOnlyDictionary<string, HandlerInfo> handlersBySimple)
    {
        sb.AppendLine("## Architectural DNA & Conventions");
        sb.AppendLine();

        AppendTopology(sb, result);
        AppendConventions(sb, result, handlersByFull, handlersBySimple);

        sb.AppendLine();
    }

    private static void AppendTopology(StringBuilder sb, AnalysisResult result)
    {
        if (result.Projects.Count == 0)
            return;

        sb.AppendLine("### Layer & Project Topology");
        foreach (var p in result.Projects.OrderBy(p => p.Layer, StringComparer.Ordinal).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            var refs = p.ReferenceNames.Count > 0
                ? $" → references {string.Join(", ", p.ReferenceNames.OrderBy(r => r, StringComparer.OrdinalIgnoreCase).Select(r => $"`{r}`"))}"
                : " (leaf project)";
            sb.AppendLine($"- **`{p.Name}`** [Layer: `{p.Layer}`]{refs}");
        }
        sb.AppendLine();
    }

    private static void AppendConventions(
        StringBuilder sb,
        AnalysisResult result,
        IReadOnlyDictionary<string, HandlerInfo> handlersByFull,
        IReadOnlyDictionary<string, HandlerInfo> handlersBySimple)
    {
        sb.AppendLine("### Codebase Conventions");
        var mediatorLabel = result.Conventions.Count > 0
            ? string.Join(", ", result.Conventions)
            : "not detected (no mediator/dispatch convention found in source)";
        sb.AppendLine($"- **Mediator / Dispatch**: `{mediatorLabel}`");

        if (result.Requests.Count > 0)
        {
            AppendSliceLayout(sb, result, handlersByFull, handlersBySimple);
            AppendRequestProjects(sb, result);
        }

        if (result.Behaviors.Count > 0)
        {
            var behaviors = string.Join(", ", result.Behaviors
                .OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
                .Select(b => $"`{b.Name}`{FormatLoc(b.Location)}"));
            sb.AppendLine($"- **Pipeline Behaviors**: {behaviors}");
        }

        if (result.DbContexts.Count > 0)
        {
            var contexts = string.Join(", ", result.DbContexts
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .Select(c => $"`{c.Name}` ({c.DbSets.Count} DbSet(s){FormatLoc(c.Location)})"));
            sb.AppendLine($"- **Data Contexts**: {contexts}");
        }
    }

    private static void AppendSliceLayout(
        StringBuilder sb,
        AnalysisResult result,
        IReadOnlyDictionary<string, HandlerInfo> handlersByFull,
        IReadOnlyDictionary<string, HandlerInfo> handlersBySimple)
    {
        var colocatedCount = 0;
        var linkedCount = 0;
        foreach (var r in result.Requests)
        {
            if (r.Location is null || r.Handlers.Count == 0) continue;
            var h = Resolve(r.Handlers[0], handlersByFull, handlersBySimple);
            if (h?.Location is null) continue;
            linkedCount++;
            if (string.Equals(r.Location.File, h.Location.File, StringComparison.OrdinalIgnoreCase))
                colocatedCount++;
        }

        if (linkedCount == 0)
            return;

        var layoutStyle = colocatedCount * 2 >= linkedCount
            ? $"Co-located vertical slices ({colocatedCount}/{linkedCount} requests share a file with their handler)"
            : $"Separate request and handler files ({linkedCount - colocatedCount}/{linkedCount} handlers in dedicated files)";
        sb.AppendLine($"- **Slice File Layout**: {layoutStyle}");
    }

    private static void AppendRequestProjects(StringBuilder sb, AnalysisResult result)
    {
        var reqProjects = result.Requests
            .Where(r => !string.IsNullOrEmpty(r.ProjectName))
            .Select(r => r.ProjectName!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (reqProjects.Count > 0)
            sb.AppendLine($"- **Request Contracts Live In**: {string.Join(", ", reqProjects.Select(p => $"`{p}`"))}");
    }
}
