namespace CqrsLens;

/// <summary>Key and relationship configuration collected from Fluent API calls.</summary>
internal sealed class EfFluentConfiguration
{
    public List<EfKeyConfiguration> Keys { get; } = new();
    public List<EfRelationshipConfiguration> Relationships { get; } = new();
}
