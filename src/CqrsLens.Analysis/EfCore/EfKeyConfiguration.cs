namespace CqrsLens;

internal sealed record EfKeyConfiguration(string EntityFullName, IReadOnlyList<string> Properties);
