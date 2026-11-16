namespace CqrsLens;

/// <summary>Declared forbidden directional call boundary between two modules (R4).</summary>
public sealed record IntentBoundaryRule(
    string FromModule,
    string ToModule,
    CheckSeverity Severity = CheckSeverity.Warning);
