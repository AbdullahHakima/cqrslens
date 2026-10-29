namespace CqrsLens;

/// <summary>Headline counts for an architecture diff.</summary>
internal sealed class DiffSummary
{
    public DiffMetric Projects { get; set; } = new(0, 0);
    public DiffMetric Requests { get; set; } = new(0, 0);
    public DiffMetric Handlers { get; set; } = new(0, 0);
    public DiffMetric Validators { get; set; } = new(0, 0);
    public DiffMetric Behaviors { get; set; } = new(0, 0);
    public DiffMetric DbContexts { get; set; } = new(0, 0);
    public DiffMetric Errors { get; set; } = new(0, 0);
    public DiffMetric Warnings { get; set; } = new(0, 0);
}
