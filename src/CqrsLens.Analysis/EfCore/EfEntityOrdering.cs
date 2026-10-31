namespace CqrsLens;

/// <summary>Canonical ordering for entities in an EF Core model.</summary>
internal static class EfEntityOrdering
{
    internal static int ByFullName(EntityInfo left, EntityInfo right)
        => string.Compare(left.FullName, right.FullName, StringComparison.Ordinal);
}
