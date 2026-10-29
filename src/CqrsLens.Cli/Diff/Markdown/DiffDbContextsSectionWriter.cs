using System.Text;

using static CqrsLens.JsonReportGenerator;

namespace CqrsLens;

/// <summary>Writes the DbContext, DbSet, entity and relationship changes section.</summary>
internal static class DiffDbContextsSectionWriter
{
    public static void Write(StringBuilder markdown, DbContextDiffSection dbContexts)
    {
        if (!dbContexts.HasChanges)
            return;

        markdown.AppendLine("### Data Access Changes (DbContexts)");
        markdown.AppendLine();

        foreach (var context in dbContexts.Added)
        {
            var sets = context.DbSets.Count > 0
                ? string.Join(", ", context.DbSets.Select(set => $"{set.Property} → {set.Entity}"))
                : "no DbSets";
            markdown.AppendLine(
                $"- ➕ Added DbContext **{ReportGenerator.InlineText(context.Name)}** " +
                $"({ReportGenerator.InlineText(context.Project ?? "—")}): {ReportGenerator.InlineText(sets)}");
        }

        foreach (var context in dbContexts.Removed)
        {
            markdown.AppendLine(
                $"- ➖ Removed DbContext **{ReportGenerator.InlineText(context.Name)}** " +
                $"({ReportGenerator.InlineText(context.Project ?? "—")})");
        }

        foreach (var context in dbContexts.Modified)
        {
            var parts = new List<string>();
            if (context.AddedDbSets.Count > 0)
                parts.Add($"+DbSets: {string.Join(", ", context.AddedDbSets)}");
            if (context.RemovedDbSets.Count > 0)
                parts.Add($"-DbSets: {string.Join(", ", context.RemovedDbSets)}");
            if (context.AddedEntities.Count > 0)
                parts.Add($"+Entities: {string.Join(", ", context.AddedEntities.Select(CSharpScanner.SimpleName))}");
            if (context.RemovedEntities.Count > 0)
                parts.Add($"-Entities: {string.Join(", ", context.RemovedEntities.Select(CSharpScanner.SimpleName))}");
            if (context.AddedRelationships.Count > 0)
                parts.Add($"+Relationships: {string.Join(", ", context.AddedRelationships)}");
            if (context.RemovedRelationships.Count > 0)
                parts.Add($"-Relationships: {string.Join(", ", context.RemovedRelationships)}");
            markdown.AppendLine(
                $"- 🔄 Modified DbContext **{ReportGenerator.InlineText(context.Name)}**: " +
                $"{ReportGenerator.InlineText(string.Join("; ", parts))}");
        }

        markdown.AppendLine();
    }
}
