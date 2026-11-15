namespace CqrsLens;

/// <summary>
/// Provenance of a project's layer assignment: explicitly configured in user settings
/// or heuristically guessed from project naming/folder structure (F0 §6).
/// </summary>
public enum LayerProvenance
{
    Configured,
    Guessed,
}
