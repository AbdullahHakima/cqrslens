using System.Text;
using static CqrsLens.AgentArchitectureDnaWriter;
using static CqrsLens.AgentReportFormatter;
using static CqrsLens.AgentReportLimits;
using static CqrsLens.AgentReportLookups;

namespace CqrsLens;

/// <summary>Generates the entity-impact trace for MCP consumers.</summary>
internal static class AgentEntityImpactGenerator
{
    internal static string Generate(
        AnalysisResult result,
        string entityFilter,
        bool containsFilter = false,
        int maxResults = 0)
    {
        ArgumentNullException.ThrowIfNull(result);
        var filter = (entityFilter ?? "").Trim();
        var sb = new StringBuilder();
        var rootName = new DirectoryInfo(result.RootPath).Name;

        sb.AppendLine($"# CqrsLens Entity Impact Trace — `{filter}` (`{rootName}`)");
        sb.AppendLine();

        var (handlersByFull, handlersBySimple) = BuildLookups(result.Handlers, h => h.FullName, h => h.Name);

        bool MatchesEntityLabel(string label)
        {
            if (containsFilter)
                return label.Contains(filter, StringComparison.OrdinalIgnoreCase);
            var entity = EntityNameFromLabel(label);
            var property = EntityPropertyFromLabel(label);
            return entity.Equals(filter, StringComparison.OrdinalIgnoreCase) ||
                   property.Equals(filter, StringComparison.OrdinalIgnoreCase);
        }

        bool MatchesType(string? type)
        {
            if (string.IsNullOrEmpty(type)) return false;
            return containsFilter
                ? type.Contains(filter, StringComparison.OrdinalIgnoreCase)
                : TypeContainsName(type, filter);
        }

        var impactedRequests = result.Requests
            .Where(r =>
                r.TouchedEntities.Any(MatchesEntityLabel) ||
                MatchesType(r.ResponseType) ||
                r.Properties.Any(p => MatchesType(p.Type)) ||
                r.Handlers.Any(hName =>
                {
                    var h = Resolve(hName, handlersByFull, handlersBySimple);
                    return h is not null &&
                           (h.TouchedEntities.Any(MatchesEntityLabel) ||
                            h.Dependencies.Any(MatchesType));
                }))
            .ToList();

        var limit = maxResults > 0 ? Math.Min(maxResults, 50) : 25;
        var shownRequests = impactedRequests.Take(limit).ToList();

        AppendDataModelSummary(sb, result, filter, impactedRequests, exactFilter: !containsFilter);

        sb.AppendLine(shownRequests.Count == impactedRequests.Count
            ? $"## Impacted Vertical Slices ({impactedRequests.Count})"
            : $"## Impacted Vertical Slices (showing {shownRequests.Count} of {impactedRequests.Count})");
        sb.AppendLine();
        if (impactedRequests.Count == 0)
        {
            sb.AppendLine($"_No commands or queries directly touch or depend on `{filter}`._");
            var known = KnownEntityNames(result);
            if (known.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"Known entities: {JoinLimited(known, 20, e => $"`{e}`")}. " +
                              (containsFilter ? "" : "Use `match: \"contains\"` for a substring search."));
            }
        }
        else
        {
            foreach (var r in shownRequests)
            {
                var response = string.IsNullOrEmpty(r.ResponseType) ? "void" : r.ResponseType;
                var ep = r.Entrypoints.Count > 0
                    ? $" · Entrypoints: {JoinLimited(r.Entrypoints, 3, e => $"`{e}`")}"
                    : "";
                sb.AppendLine($"- **[{r.Kind}] `{r.Name}`** → `{response}`{FormatLoc(r.Location)}{ep}");
                foreach (var hName in r.Handlers.Take(3))
                {
                    var h = Resolve(hName, handlersByFull, handlersBySimple);
                    if (h is null) continue;
                    var deps = h.Dependencies.Count > 0
                        ? $" · Deps: {JoinLimited(h.Dependencies, MaxListItems, d => $"`{d}`")}"
                        : "";
                    var sets = h.TouchedEntities.Count > 0
                        ? $" · DbSets: {JoinLimited(h.TouchedEntities, MaxListItems, e => $"`{e}`")}"
                        : "";
                    sb.AppendLine($"  - Handler: `{h.Name}`{FormatLoc(h.Location)}{deps}{sets}");
                }
            }

            if (impactedRequests.Count > shownRequests.Count)
                sb.AppendLine($"_+{impactedRequests.Count - shownRequests.Count} more impacted slice(s) — narrow the entity or raise `limit`._");
        }
        sb.AppendLine();

        return sb.ToString();
    }

    private static List<string> KnownEntityNames(AnalysisResult result)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var context in result.DbContexts)
        {
            foreach (var set in context.DbSets)
                names.Add(CSharpScanner.SimpleName(set.Entity));
            foreach (var entity in context.Entities)
                names.Add(entity.Name);
        }
        foreach (var label in result.Requests.SelectMany(r => r.TouchedEntities))
            names.Add(EntityNameFromLabel(label));
        return names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
    }

}