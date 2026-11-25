namespace CqrsLens;

internal sealed record SliceTestSuggestion(
    string Boundary, string Level, string Target, string Purpose, IReadOnlyList<SliceTestEvidence> Evidence);
