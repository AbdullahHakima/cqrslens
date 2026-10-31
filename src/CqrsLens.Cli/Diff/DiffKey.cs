namespace CqrsLens;

/// <summary>Builds the composite keys used to match diff entries across scans.</summary>
internal static class DiffKey
{
    private const char Separator = '\u001f';

    public static string Combine(params string?[] parts) => string.Join(Separator, parts);
}
