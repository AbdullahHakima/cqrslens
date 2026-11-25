namespace CqrsLens;

/// <summary>A planned HTTP method and route template for the slice.</summary>
public sealed record SliceDesignEndpoint(string Method, string Route);
