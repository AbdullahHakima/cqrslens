using System.Text;
using static CqrsLens.AgentArchitectureDnaWriter;
using static CqrsLens.AgentReportFormatter;
using static CqrsLens.AgentReportLookups;

namespace CqrsLens;

/// <summary>Generates the compact Architectural DNA and optional slice index.</summary>
internal static class AgentDnaIndexGenerator
{
    internal static string Generate(AnalysisResult result, bool includeIndex = false)
    {
        ArgumentNullException.ThrowIfNull(result);

        var rootName = new DirectoryInfo(result.RootPath).Name;
        var sb = new StringBuilder();

        var (handlersByFull, handlersBySimple) = BuildLookups(result.Handlers, h => h.FullName, h => h.Name);

        sb.AppendLine($"# CqrsLens Architectural DNA & Slice Index — `{rootName}`");
        sb.AppendLine();
        sb.AppendLine($"> {result.Projects.Count} project(s), {result.Endpoints.Count} endpoint(s), " +
                      $"{result.Requests.Count} request(s), {result.Handlers.Count} handler(s), " +
                      $"{result.Validators.Count} validator(s), {result.Behaviors.Count} behavior(s), " +
                      $"{result.Notifications.Count} notification(s), {result.DbContexts.Count} DbContext(s).");
        sb.AppendLine();

        AppendArchitecturalDna(sb, result, handlersByFull, handlersBySimple);

        sb.AppendLine($"## Vertical Slice Index ({result.Requests.Count})");
        sb.AppendLine();
        if (!includeIndex)
        {
            sb.AppendLine($"_Index omitted to keep this response compact. Call `get_architecture_dna` with `includeIndex: true` to list all {result.Requests.Count} slice(s), or use `trace_slice` with a domain noun._");
            sb.AppendLine();
            return sb.ToString();
        }

        if (result.Requests.Count == 0)
        {
            sb.AppendLine("_No CQRS requests discovered._");
        }
        else
        {
            foreach (var r in result.Requests.Take(200))
            {
                var response = string.IsNullOrEmpty(r.ResponseType) ? "void" : r.ResponseType;
                var ep = r.Entrypoints.Count > 0 ? $" [{string.Join(", ", r.Entrypoints.Take(3))}]" : "";
                var proj = string.IsNullOrEmpty(r.ProjectName) ? "" : $" (`{r.ProjectName}`)";
                sb.AppendLine($"- **[{r.Kind}] `{r.Name}`** → `{response}`{ep}{proj}{FormatLoc(r.Location)}");
            }
            if (result.Requests.Count > 200)
                sb.AppendLine($"- _+{result.Requests.Count - 200} more slice(s); use `trace_slice` to narrow._");
        }
        sb.AppendLine();

        if (result.Notifications.Count > 0)
        {
            sb.AppendLine($"## Domain Events & Notifications Index ({result.Notifications.Count})");
            sb.AppendLine();
            foreach (var n in result.Notifications.Take(100))
            {
                sb.AppendLine($"- **`{n.Name}`** ({n.Handlers.Count} handler(s)){FormatLoc(n.Location)}");
            }
            if (result.Notifications.Count > 100)
                sb.AppendLine($"- _+{result.Notifications.Count - 100} more notification(s)._");
            sb.AppendLine();
        }

        return sb.ToString();
    }
}