namespace CqrsLens;

/// <summary>Runtime role classification for a scanned project or service.</summary>
public enum ServiceRuntimeRole
{
    Api,
    Worker,
    Ui,
    Orchestrator,
    Library,
    Tests,
}
