namespace CqrsLens;

/// <summary>A developer-authored acceptance scenario to verify through tests.</summary>
public sealed record SliceDesignScenario(string Name, string Given, string When, string Then);
