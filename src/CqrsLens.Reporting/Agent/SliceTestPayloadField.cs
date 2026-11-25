namespace CqrsLens;

/// <summary>One request payload property discovered from the scanned CQRS request.</summary>
internal sealed record SliceTestPayloadField(string Name, string Type);
