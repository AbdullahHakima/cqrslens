namespace CqrsLens;

/// <summary>A source file read from disk and attributed to its owning project.</summary>
internal sealed record SourceFile(string Path, string Code, string? ProjectName);
