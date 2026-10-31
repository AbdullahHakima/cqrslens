using System.Text;

namespace CqrsLens;

/// <summary>Writes the project and layer changes section.</summary>
internal static class DiffProjectsSectionWriter
{
    public static void Write(StringBuilder markdown, ProjectDiffSection projects)
    {
        if (!projects.HasChanges)
            return;

        markdown.AppendLine("### Project & Layer Changes");
        markdown.AppendLine();

        foreach (var project in projects.Added)
        {
            var references = project.References.Count > 0
                ? $" → references: {string.Join(", ", project.References)}"
                : "";
            markdown.AppendLine(
                $"- ➕ Added project **{ReportGenerator.InlineText(project.Name)}** " +
                $"(`{ReportGenerator.InlineText(project.Layer)}`){ReportGenerator.InlineText(references)}");
        }

        foreach (var project in projects.Removed)
        {
            markdown.AppendLine(
                $"- ➖ Removed project **{ReportGenerator.InlineText(project.Name)}** " +
                $"(`{ReportGenerator.InlineText(project.Layer)}`)");
        }

        foreach (var project in projects.Modified)
        {
            var parts = new List<string>();
            if (project.OldLayer is not null && project.NewLayer is not null)
                parts.Add($"layer `{project.OldLayer}` → `{project.NewLayer}`");
            if (project.AddedReferences.Count > 0)
                parts.Add($"+refs: {string.Join(", ", project.AddedReferences)}");
            if (project.RemovedReferences.Count > 0)
                parts.Add($"-refs: {string.Join(", ", project.RemovedReferences)}");
            if (project.AddedPackages.Count > 0)
                parts.Add($"+packages: {string.Join(", ", project.AddedPackages)}");
            if (project.RemovedPackages.Count > 0)
                parts.Add($"-packages: {string.Join(", ", project.RemovedPackages)}");
            markdown.AppendLine(
                $"- 🔄 Modified project **{ReportGenerator.InlineText(project.Name)}**: " +
                $"{ReportGenerator.InlineText(string.Join("; ", parts))}");
        }

        markdown.AppendLine();
    }
}
