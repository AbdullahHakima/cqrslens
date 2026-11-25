namespace CqrsLens;

/// <summary>A planned input property on a CQRS request.</summary>
public sealed record SliceDesignProperty(string Name, string? Type = null);
