namespace CqrsLens;

internal sealed class JsonEntityRelationship
{
    public string PrincipalEntity { get; set; } = "";
    public string DependentEntity { get; set; } = "";
    public string? PrincipalNavigation { get; set; }
    public string? DependentNavigation { get; set; }
    public string? ForeignKey { get; set; }
    public bool IsRequired { get; set; }
    public string Kind { get; set; } = "";
    public string? ConfiguredBy { get; set; }
}
