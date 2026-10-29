namespace CqrsLens;

/// <summary>Renders the human-readable list of changes on a modified request.</summary>
internal static class DiffRequestChangeFormatter
{
    public static string Format(ModifiedRequestDiff modified)
    {
        var parts = new List<string>();
        if (modified.OldKind is not null && modified.NewKind is not null)
            parts.Add($"Kind: {modified.OldKind} → {modified.NewKind}");
        if (modified.OldResponseType != modified.NewResponseType)
            parts.Add($"Response: {modified.OldResponseType ?? "—"} → {modified.NewResponseType ?? "—"}");
        if (modified.OldProject != modified.NewProject)
            parts.Add($"Project: {modified.OldProject ?? "—"} → {modified.NewProject ?? "—"}");
        if (modified.AddedHandlers.Count > 0)
            parts.Add($"+Handler: {string.Join(", ", modified.AddedHandlers.Select(CSharpScanner.SimpleName))}");
        if (modified.RemovedHandlers.Count > 0)
            parts.Add($"-Handler: {string.Join(", ", modified.RemovedHandlers.Select(CSharpScanner.SimpleName))}");
        if (modified.AddedValidators.Count > 0)
            parts.Add($"+Validator: {string.Join(", ", modified.AddedValidators.Select(CSharpScanner.SimpleName))}");
        if (modified.RemovedValidators.Count > 0)
            parts.Add($"-Validator: {string.Join(", ", modified.RemovedValidators.Select(CSharpScanner.SimpleName))}");
        return string.Join("; ", parts);
    }
}
