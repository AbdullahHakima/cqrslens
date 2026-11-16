namespace CqrsLens;

/// <summary>Declared intended public surface of endpoints and/or components (R4).</summary>
public sealed record IntentPublicSurface(
    IReadOnlyList<string>? Endpoints,
    IReadOnlyList<string>? Components,
    CheckSeverity Severity = CheckSeverity.Warning);
