using System.Globalization;
using System.Text;

namespace CqrsLens;

/// <summary>Shared deterministic Markdown formatting and diagram naming helpers for exported docs pages.</summary>
internal static class DocsFormatting
{
    internal static void Line(StringBuilder md, string text = "")
    {
        md.Append(text);
        md.Append('\n');
    }

    internal static void WriteHeader(StringBuilder md, string title, AnalysisResult result)
    {
        var generatedAt = result.HasExplicitGeneratedAt
            ? result.GeneratedAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
            : "unpinned";
        var rootName = new DirectoryInfo(result.RootPath).Name;

        Line(md, $"# {title}");
        Line(md);
        Line(
            md,
            $"> Source: generated from the model, Noctua {ToolInfo.Version}, {generatedAt} · " +
            $"root: {MarkdownEscaping.CodeSpan(rootName)}");
        Line(md);
    }

    internal static string FormatLocation(SourceLocation? location)
        => location is null ? "—" : MarkdownEscaping.CodeSpan($"{location.File}:{location.Line}");

    internal static string FormatViaPath(IReadOnlyList<NodeReference> via)
        => via.Count == 0 ? "—" : string.Join(" → ", via.Select(v => v.Name));

    internal static string FormatLayerProvenance(ProjectNode project)
        => project.LayerProvenance == LayerProvenance.Configured
            ? "configured"
            : "guessed (inferred)";

    internal static string FormatConfigPath(AnalysisResult result, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "No config file found";

        if (!string.IsNullOrWhiteSpace(result.RootPath))
        {
            var rel = Path.GetRelativePath(result.RootPath, path).Replace('\\', '/');
            if (!rel.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(rel))
                return rel;
        }

        return Path.GetFileName(path);
    }

    internal static Dictionary<ProjectId, ProjectNode> BuildProjectsById(ArchitectureSpine spine)
    {
        var map = new Dictionary<ProjectId, ProjectNode>(spine.Projects.Count);
        foreach (var project in spine.Projects)
            map[project.Id] = project;
        return map;
    }

    internal static IReadOnlyList<string> DiagramFamilyFileNames(
        string baseName,
        IEnumerable<string> sources)
    {
        var names = new List<string>();
        var index = 0;
        foreach (var source in sources)
        {
            if (!DiagramBuilder.IsWithinMermaidLimits(source))
                continue;

            index++;
            names.Add(index == 1 ? $"{baseName}.svg" : $"{baseName}-{index}.svg");
        }

        return names;
    }

    internal static IReadOnlyList<string> SequenceDiagramFileNames(IReadOnlyList<RequestSequence> sequences)
    {
        var result = new string[sequences.Count];
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < sequences.Count; i++)
        {
            if (!DiagramBuilder.IsWithinMermaidLimits(sequences[i].Source))
            {
                result[i] = string.Empty;
                continue;
            }

            var baseName = "sequence-" + SanitizeFileName(sequences[i].RequestName);
            var name = baseName + ".svg";
            var suffix = 1;
            while (!usedNames.Add(name))
            {
                suffix++;
                name = $"{baseName}-{suffix}.svg";
            }

            result[i] = name;
        }

        return result;
    }

    internal static string SanitizeFileName(string value)
    {
        var chars = value.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '-').ToArray();
        var name = new string(chars).Trim('-');
        return name.Length == 0 ? "request" : name;
    }
}
