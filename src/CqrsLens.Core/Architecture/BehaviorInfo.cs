namespace CqrsLens;

public sealed class BehaviorInfo
{
    public required string Name { get; init; }
    public string? ProjectName { get; set; }
    public SourceLocation? Location { get; set; }
}
