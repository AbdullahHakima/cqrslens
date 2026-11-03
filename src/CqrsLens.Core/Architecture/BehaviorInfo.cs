namespace CqrsLens;

public sealed class BehaviorInfo
{
    public required string Name { get; init; }
    public string? ProjectName { get; set; }
    public SourceLocation? Location { get; set; }

    /// <summary>
    /// 1-based ordinal of this behavior among the behavior registrations at the same
    /// registration site. Null when no registration site could be resolved — the
    /// behavior's registered order is then not determinable. Presentation-only: this
    /// never feeds detection and is not part of the JSON contract.
    /// </summary>
    public int? RegistrationOrder { get; set; }

    /// <summary>
    /// The resolved registration site — relative source file and the line range of the
    /// behavior registration expressions that name this behavior, e.g.
    /// <c>Src/Application/DependencyInjection.cs:15-16</c>. Null when no registration
    /// site could be resolved (Scrutor/Autofac assembly scanning, external
    /// registrations, or an ambiguous match). Presentation-only: not part of the JSON
    /// contract.
    /// </summary>
    public string? RegistrationSite { get; set; }
}
