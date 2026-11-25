namespace CqrsLens;

internal sealed record SliceTestEvidence(string Kind, string Target, string? Project, JsonLocation? Location);
