using System.Text;

namespace CqrsLens;

/// <summary>Builds the one-line "changes detected" headline for a diff report.</summary>
internal static class DiffHeadlineBuilder
{
    public static string Build(ArchitectureDiff diff)
    {
        var items = new List<string>();
        if (diff.Requests.HasChanges)
            items.Add($"+{diff.Requests.Added.Count}/-{diff.Requests.Removed.Count}/~{diff.Requests.Modified.Count} request(s)");
        if (diff.Handlers.HasChanges)
            items.Add($"+{diff.Handlers.Added.Count}/-{diff.Handlers.Removed.Count} handler(s)");
        if (diff.Validators.HasChanges)
            items.Add($"+{diff.Validators.Added.Count}/-{diff.Validators.Removed.Count} validator(s)");
        if (diff.Behaviors.HasChanges)
            items.Add($"+{diff.Behaviors.Added.Count}/-{diff.Behaviors.Removed.Count} behavior(s)");
        if (diff.Projects.HasChanges)
            items.Add($"+{diff.Projects.Added.Count}/-{diff.Projects.Removed.Count}/~{diff.Projects.Modified.Count} project(s)");
        if (diff.DbContexts.HasChanges)
            items.Add($"+{diff.DbContexts.Added.Count}/-{diff.DbContexts.Removed.Count}/~{diff.DbContexts.Modified.Count} DbContext(s)");
        if (diff.Checks.HasChanges)
            items.Add($"+{diff.Checks.NewFindings.Count} new / -{diff.Checks.ResolvedFindings.Count} resolved check finding(s)");

        return $"🔄 **Changes detected:** {string.Join(", ", items)}.";
    }
}
