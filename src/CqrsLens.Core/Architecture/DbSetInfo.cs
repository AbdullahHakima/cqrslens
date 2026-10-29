namespace CqrsLens;

public sealed record DbSetInfo(string Entity, string Property)
{
    /// <summary>
    /// Full source name of the entity when it resolves to a scanned class. This is
    /// informational: <see cref="Entity"/> remains the type name as written in
    /// the DbSet so existing consumers keep their current behavior.
    /// </summary>
    public string? EntityFullName { get; set; }
}
